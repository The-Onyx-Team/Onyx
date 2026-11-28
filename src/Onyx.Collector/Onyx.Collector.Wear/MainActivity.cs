using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Onyx.App.UsageData;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Wear;

/// <summary>
/// The watch's only screen: link the watch (a code to enter on another device), allow usage access, see the last
/// upload. Usage itself is uploaded in the background (<see cref="WearSyncWorker"/>).
/// </summary>
[Activity(Label = "Onyx", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.DeviceDefault")]
public class MainActivity : Activity
{
    private static readonly Color s_Text = Color.ParseColor("#F6F2E9");
    private static readonly Color s_Muted = Color.ParseColor("#9AA0A8");
    private static readonly Color s_Accent = Color.ParseColor("#C58848");

    private LinearLayout m_Content = null!;
    private CancellationTokenSource? m_Polling;
    private bool m_Busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var density = Resources!.DisplayMetrics!.Density;
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.SetBackgroundColor(Color.Black);
        m_Content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        m_Content.SetGravity(GravityFlags.CenterHorizontal);
        // Round screens: keep text away from the edge.
        var side = (int)(Resources.DisplayMetrics.WidthPixels * 0.12);
        m_Content.SetPadding(side, (int)(24 * density), side, (int)(32 * density));
        scroll.AddView(m_Content);
        SetContentView(scroll);
    }

    protected override async void OnResume()
    {
        base.OnResume();
        await RenderAsync();
    }

    protected override void OnPause()
    {
        base.OnPause();
        m_Polling?.Cancel();
    }

    private async Task RenderAsync()
    {
        m_Content.RemoveAllViews();
        Text("Onyx", 18, s_Accent, bold: true);

        if (!await WearServices.Client.IsSignedInAsync())
        {
            Text("Link this watch to your Onyx account. You'll enter a code on your phone or computer.", 13, s_Text);
            var server = new EditText(this) { Text = WearServices.Server, TextSize = 12 };
            server.SetTextColor(s_Muted);
            server.SetSingleLine(true);
            m_Content.AddView(server, Wide());
            Button("Get a code", async () =>
            {
                WearServices.Server = server.Text ?? "";
                await LinkAsync();
            });
            return;
        }

        if (!AndroidUsageReader.HasUsageAccess())
        {
            Text("Onyx needs usage access to see which apps you use on the watch.", 13, s_Text);
            Button("Allow usage access", OpenUsageAccess);
        }

        Text(WearSync.LastUpload is { } last ? $"Last upload: {last.ToLocalTime():g}" : "Not uploaded yet", 13, s_Text);
        if (WearSync.LastError is { } error)
            Text(error, 12, s_Accent);
        Button("Upload now", async () =>
        {
            await WearSync.SyncAsync();
            await RenderAsync();
        });
        Button("Unlink", async () =>
        {
            await WearServices.Client.LogoutAsync();
            WearSync.Reset();
            await RenderAsync();
        });
    }

    /// <summary>Shows a code and waits until the user entered it on another device (or it expired).</summary>
    private async Task LinkAsync()
    {
        m_Polling?.Cancel();
        var polling = m_Polling = new CancellationTokenSource();
        m_Content.RemoveAllViews();
        Text("Onyx", 18, s_Accent, bold: true);

        try
        {
            var client = WearServices.Client;
            var (privateKey, publicKey) = await client.Crypto.CreateKeyPairAsync();
            var link = await client.StartLinkAsync(publicKey, polling.Token);

            Text("Enter this code in Onyx → Devices → Link a watch:", 13, s_Text);
            Text(DeviceLink.Display(link.Code), 22, s_Text, bold: true, monospace: true);
            Text("Waiting…", 12, s_Muted);
            Button("Cancel", async () =>
            {
                polling.Cancel();
                await RenderAsync();
            });

            while (!polling.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), polling.Token);
                if (await client.CompleteLinkAsync(link, privateKey, publicKey, polling.Token))
                {
                    WearSync.Reset();
                    await RenderAsync();
                    _ = WearSync.SyncAsync();
                    return;
                }
            }
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            m_Content.RemoveAllViews();
            Text("Onyx", 18, s_Accent, bold: true);
            Text(e is NotSignedInException ? "The code expired." : $"Could not reach {WearServices.Server}: {e.Message}", 13, s_Accent);
            Button("Try again", RenderAsync);
        }
    }

    private Task OpenUsageAccess()
    {
        try
        {
            StartActivity(new Intent(Settings.ActionUsageAccessSettings).AddFlags(ActivityFlags.NewTask));
        }
        catch (ActivityNotFoundException)
        {
            // Some watches have no such screen; it can be granted from a computer instead.
            Text($"This watch has no settings screen for it. From a computer: adb shell appops set {PackageName} GET_USAGE_STATS allow",
                11, s_Muted);
        }

        return Task.CompletedTask;
    }

    private void Text(string text, float size, Color color, bool bold = false, bool monospace = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size, Gravity = GravityFlags.Center };
        view.SetTextColor(color);
        view.SetTypeface(monospace ? Typeface.Monospace : Typeface.Default, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        m_Content.AddView(view, Wide());
    }

    private void Button(string text, Func<Task> action)
    {
        var button = new Android.Widget.Button(this) { Text = text };
        button.Click += async (_, _) =>
        {
            if (m_Busy)
                return;
            m_Busy = true;
            try
            {
                await action();
            }
            finally
            {
                m_Busy = false;
            }
        };
        m_Content.AddView(button, Wide());
    }

    private LinearLayout.LayoutParams Wide() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = (int)(8 * Resources!.DisplayMetrics!.Density) };
}
