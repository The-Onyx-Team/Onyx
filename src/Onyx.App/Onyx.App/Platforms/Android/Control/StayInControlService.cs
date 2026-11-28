using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views.Accessibility;
using Microsoft.Extensions.DependencyInjection;
using Onyx.App.UsageData;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Control;
using Onyx.Collector.Core.Crypto;
using Application = Android.App.Application;

namespace Onyx.App.Control;

/// <summary>
/// Stay in control mode: notices which app comes to the front (an accessibility service, the only way Android
/// tells an app this immediately) and shows <see cref="ControlActivity"/> over it when a question is due. It only
/// receives window changes, never their content. The user turns it on in Android's accessibility settings.
/// </summary>
// A fixed Java name: Android remembers an enabled accessibility service by it.
[Service(Name = "onyx.control.StayInControlService", Label = "@string/stay_in_control_label", Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/stay_in_control")]
public class StayInControlService : AccessibilityService
{
    /// <summary>Settings are read from the server at most this often (the reminder worker also refreshes them).</summary>
    private static readonly TimeSpan s_RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>Which apps were answered for, shared with the question screen.</summary>
    public static StayInControl Gate { get; } = new(TimeProvider.System);

    private readonly Dictionary<string, (bool Launchable, string? Category)> m_Apps = new();
    private HashSet<string> m_Exempt = [];
    private VaultSettings? m_Settings;
    private DateTime m_SettingsTime;
    private DateTime m_LastRefresh;
    private int? m_DeviceId;

    /// <summary>Whether the user turned the service on in Android's settings.</summary>
    public static bool IsEnabled()
    {
        var context = Application.Context;
        var enabled = Settings.Secure.GetString(context.ContentResolver, Settings.Secure.EnabledAccessibilityServices);
        if (string.IsNullOrEmpty(enabled))
            return false;
        var me = new ComponentName(context, Java.Lang.Class.FromType(typeof(StayInControlService)));
        return enabled.Split(':').Any(entry => ComponentName.UnflattenFromString(entry) is { } c && c.Equals(me));
    }

    public static void OpenSettings()
    {
        var intent = new Intent(Settings.ActionAccessibilitySettings);
        intent.SetFlags(ActivityFlags.NewTask);
        Application.Context.StartActivity(intent);
    }

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        m_Exempt = AlwaysExempt();
        _ = LoadDeviceIdAsync();
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e is not { EventType: EventTypes.WindowStateChanged } || e.PackageName?.ToString() is not { } package
            || m_Exempt.Contains(package))
            return;

        var control = CurrentSettings()?.Control;
        if (control is not { Enabled: true })
            return;

        // Only apps the user opens: system dialogs, keyboards and overlays come from packages without a launcher entry.
        var (launchable, reported) = AppInfo(package);
        if (!launchable)
            return;

        var identifier = "android:" + package;
        if (!Gate.ShouldAsk(control, m_DeviceId, identifier, m_Settings!.CategoryOf(identifier, reported)))
            return;

        var intent = new Intent(this, typeof(ControlActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.NoAnimation);
        intent.PutExtra(ControlActivity.PackageExtra, package);
        StartActivity(intent);
    }

    public override void OnInterrupt()
    {
    }

    /// <summary>The saved settings, re-read when the file changed; refreshed from the server in the background.</summary>
    private VaultSettings? CurrentSettings()
    {
        var fileTime = AndroidReminders.SavedSettingsTime;
        if (m_Settings is null || fileTime != m_SettingsTime)
        {
            m_Settings = AndroidReminders.ReadSavedSettings();
            m_SettingsTime = fileTime;
        }

        if (DateTime.UtcNow - m_LastRefresh > s_RefreshInterval)
        {
            m_LastRefresh = DateTime.UtcNow;
            _ = RefreshAsync();
        }

        return m_Settings;
    }

    private static async Task RefreshAsync()
    {
        try
        {
            if (IPlatformApplication.Current?.Services.GetService<AndroidReminders>() is { } reminders)
                await reminders.RefreshSettingsAsync();
        }
        catch (Exception)
        {
            // The saved settings stay in use.
        }
    }

    private async Task LoadDeviceIdAsync()
    {
        try
        {
            if (IPlatformApplication.Current?.Services.GetService<ICredentialStore>() is { } store)
                m_DeviceId = (await store.LoadAsync())?.DeviceId;
        }
        catch (Exception)
        {
            // Without the id, settings limited to certain devices don't apply here.
        }
    }

    private (bool Launchable, string? Category) AppInfo(string package)
    {
        if (!m_Apps.TryGetValue(package, out var info))
        {
            var launchable = PackageManager!.GetLaunchIntentForPackage(package) is not null;
            m_Apps[package] = info = (launchable, launchable ? AndroidUsageReader.Describe(package).Category : null);
        }

        return info;
    }

    /// <summary>Onyx, the home screen, the system UI, settings, keyboards and the phone (emergency calls) never ask.</summary>
    private HashSet<string> AlwaysExempt()
    {
        var exempt = new HashSet<string> { PackageName!, "com.android.systemui", "com.android.settings" };
        var pm = PackageManager!;
        foreach (var intent in new[]
                 {
                     new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome),
                     new Intent(Intent.ActionDial)
                 })
        foreach (var info in pm.QueryIntentActivities(intent, PackageInfoFlags.MatchDefaultOnly))
            if (info.ActivityInfo?.PackageName is { } name)
                exempt.Add(name);

        if (GetSystemService(InputMethodService) is Android.Views.InputMethods.InputMethodManager imm)
            foreach (var method in imm.EnabledInputMethodList ?? [])
                if (method.PackageName is { } name)
                    exempt.Add(name);

        return exempt;
    }
}
