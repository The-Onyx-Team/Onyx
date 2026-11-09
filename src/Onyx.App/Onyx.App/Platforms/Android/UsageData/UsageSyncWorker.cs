using Android.Content;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Onyx.App.UsageData;

/// <summary>
/// Periodic background sync via WorkManager. Android schedules it (at most every 15 minutes, only with network)
/// and keeps the schedule across reboots, so no foreground service or boot receiver is needed.
/// </summary>
public class UsageSyncWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private const string UniqueName = "onyx-usage-sync";

    public static void Schedule(Context context)
    {
        var request = new PeriodicWorkRequest.Builder(typeof(UsageSyncWorker), TimeSpan.FromMinutes(30))
            .SetConstraints(new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!).Build())
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(UniqueName, ExistingPeriodicWorkPolicy.Keep!, request);
    }

    public override Result DoWork()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services?.GetService<AndroidUsageSync>() is not { } sync)
            return Result.InvokeRetry();

        var ok = sync.SyncAsync().GetAwaiter().GetResult();
        return ok ? Result.InvokeSuccess() : Result.InvokeRetry();
    }
}
