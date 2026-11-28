using Android.Content;
using AndroidX.Work;
using Onyx.App.UsageData;
using Onyx.Collector.Core;

namespace Onyx.Collector.Wear;

/// <summary>One upload: the hours since the last confirmed one, read from Android's usage log (like the phone).</summary>
public static class WearSync
{
    private const string SyncedUntilKey = "synced_until";
    private const string LastUploadKey = "last_upload";
    private const string LastErrorKey = "last_error";

    /// <summary>First upload after linking: what the watch still has (Android keeps about a week).</summary>
    private static readonly TimeSpan s_FirstSyncBackfill = TimeSpan.FromDays(7);

    private static readonly SemaphoreSlim s_Lock = new(1, 1);

    public static DateTimeOffset? LastUpload =>
        WearServices.State.GetLong(LastUploadKey, 0) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public static string? LastError => WearServices.State.GetString(LastErrorKey, null);

    /// <returns>False when it should be retried later (network).</returns>
    public static async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        if (!AndroidUsageReader.HasUsageAccess())
            return Fail("Usage access has not been granted");

        var client = WearServices.Client;
        if (!await client.IsSignedInAsync())
            return Fail("Not linked");

        await s_Lock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var stored = WearServices.State.GetLong(SyncedUntilKey, 0);
            var from = stored > 0 ? new DateTime(stored, DateTimeKind.Utc) : UsageBuckets.HourOf(now - s_FirstSyncBackfill);

            var entries = WearServices.Reader.ReadBuckets(from, now);
            if (entries.Count > 0)
                await client.UploadUsageAsync(entries, WearServices.DeviceName, WearServices.Platform, WearServices.Reader, ct);

            WearServices.State.Edit()!
                .PutLong(SyncedUntilKey, UsageBuckets.HourOf(now).Ticks)!
                .PutLong(LastUploadKey, now.UtcTicks)!
                .Remove(LastErrorKey)!
                .Apply();
            return true;
        }
        catch (NotSignedInException e)
        {
            return Fail(e.Message);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Fail(e.Message);
            return false;
        }
        finally
        {
            s_Lock.Release();
        }
    }

    /// <summary>Forgets the last upload, so a new link uploads the past week again.</summary>
    public static void Reset() => WearServices.State.Edit()!
        .Remove(SyncedUntilKey)!.Remove(LastUploadKey)!.Remove(LastErrorKey)!.Apply();

    private static bool Fail(string message)
    {
        WearServices.State.Edit()!.PutString(LastErrorKey, message)!.Apply();
        return true;
    }
}

/// <summary>Uploads every 30 minutes when the watch has a connection (its own or through the phone).</summary>
public class WearSyncWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private const string UniqueName = "onyx-wear-sync";

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!)!.Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(WearSyncWorker), TimeSpan.FromMinutes(30))
            .SetConstraints(constraints)!
            .Build()!;
        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(UniqueName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    public override Result DoWork() =>
        WearSync.SyncAsync().GetAwaiter().GetResult() ? Result.InvokeSuccess() : Result.InvokeRetry();
}
