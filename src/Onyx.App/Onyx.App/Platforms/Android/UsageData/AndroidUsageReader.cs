using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;
using Application = Android.App.Application;

namespace Onyx.App.UsageData;

/// <summary>
/// Turns Android's usage event log into hourly foreground totals per app. Android records the events itself
/// (and keeps them for several days), so the app only has to read them when it syncs; nothing runs in between.
/// </summary>
public class AndroidUsageReader : IAppIconSource
{
    // UsageEvents.Event types, compared as numbers so they don't depend on binding names.
    private const int ActivityResumed = 1;
    private const int ActivityPaused = 2;
    private const int ScreenNonInteractive = 16;
    private const int KeyguardShown = 17;
    private const int ActivityStopped = 23;
    private const int DeviceShutdown = 26;

    /// <summary>Events are read from this long before the first bucket, so apps opened earlier are seen.</summary>
    private static readonly TimeSpan s_LookBehind = TimeSpan.FromHours(6);

    private static Context Context => Application.Context;

    public static bool HasUsageAccess()
    {
        var appOps = (AppOpsManager)Context.GetSystemService(Context.AppOpsService)!;
        var mode = OperatingSystem.IsAndroidVersionAtLeast(29)
            ? appOps.UnsafeCheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!, Android.OS.Process.MyUid(), Context.PackageName!)
#pragma warning disable CA1422
            : appOps.CheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!, Android.OS.Process.MyUid(), Context.PackageName!);
#pragma warning restore CA1422
        return mode == AppOpsManagerMode.Allowed;
    }

    /// <summary>Hourly buckets from <paramref name="fromHour"/> (UTC) up to now.</summary>
    public List<UsageEntryDto> ReadBuckets(DateTime fromHour, DateTimeOffset now)
    {
        var manager = (UsageStatsManager)Context.GetSystemService(Context.UsageStatsService)!;
        var queryStart = new DateTimeOffset(fromHour) - s_LookBehind;
        var events = manager.QueryEvents(queryStart.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());
        if (events is null)
            return [];

        var ignored = IgnoredPackages();
        var apps = new Dictionary<string, ForegroundApp>();
        var intervals = new List<(ForegroundApp, DateTimeOffset, DateTimeOffset)>();

        // An app counts as in front from the first resumed activity until the last one is paused or stopped.
        var resumed = new Dictionary<string, HashSet<string>>();
        var since = new Dictionary<string, DateTimeOffset>();

        void Close(string package, DateTimeOffset at)
        {
            resumed.Remove(package);
            if (since.Remove(package, out var start) && at > start && !ignored.Contains(package))
            {
                if (!apps.TryGetValue(package, out var app))
                    apps[package] = app = Describe(package);
                intervals.Add((app, start, at));
            }
        }

        var evt = new UsageEvents.Event();
        while (events.HasNextEvent)
        {
            events.GetNextEvent(evt);
            var package = evt.PackageName;
            var at = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimeStamp);

            switch ((int)evt.EventType)
            {
                case ActivityResumed when package is not null:
                    if (!resumed.TryGetValue(package, out var activities))
                        resumed[package] = activities = [];
                    if (activities.Count == 0)
                        since[package] = at;
                    activities.Add(evt.ClassName ?? "");
                    break;

                case ActivityPaused or ActivityStopped when package is not null:
                    if (resumed.TryGetValue(package, out var open))
                    {
                        open.Remove(evt.ClassName ?? "");
                        if (open.Count == 0)
                            Close(package, at);
                    }
                    break;

                case ScreenNonInteractive or KeyguardShown or DeviceShutdown:
                    foreach (var p in resumed.Keys.ToList())
                        Close(p, at);
                    break;
            }
        }

        // Still in front right now.
        foreach (var p in resumed.Keys.ToList())
            Close(p, now);

        return UsageBuckets.FromIntervals(intervals)
            .Where(e => e.BucketStart >= fromHour)
            .ToList();
    }

    public byte[]? GetIconPng(string appIdentifier)
    {
        const string prefix = "android:";
        if (!appIdentifier.StartsWith(prefix))
            return null;

        try
        {
            var icon = Context.PackageManager!.GetApplicationIcon(appIdentifier[prefix.Length..]);
            return ToPng(icon, 96);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    private static ForegroundApp Describe(string package)
    {
        var pm = Context.PackageManager!;
        try
        {
            var info = pm.GetApplicationInfo(package, 0);
            var name = info.LoadLabel(pm) ?? package;
            string? category = null;
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && info.Category != ApplicationCategories.Undefined)
                category = info.Category.ToString();
            return new ForegroundApp("android:" + package, name, category);
        }
        catch (PackageManager.NameNotFoundException)
        {
            // Uninstalled since, or not visible to this app.
            return new ForegroundApp("android:" + package, package);
        }
    }

    /// <summary>This app, the launcher (home screen) and the system UI are not "usage".</summary>
    private static HashSet<string> IgnoredPackages()
    {
        var ignored = new HashSet<string> { Context.PackageName!, "com.android.systemui" };

        var home = new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome);
        foreach (var info in Context.PackageManager!.QueryIntentActivities(home, PackageInfoFlags.MatchDefaultOnly))
            if (info.ActivityInfo?.PackageName is { } launcher)
                ignored.Add(launcher);

        return ignored;
    }

    private static byte[] ToPng(Drawable drawable, int size)
    {
        using var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, size, size);
        drawable.Draw(canvas);

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
    }
}
