using Android.Content;
using Android.Provider;
using Onyx.App.Shared.Services.Usage;
using Application = Android.App.Application;

namespace Onyx.App.UsageData;

public class AndroidStatsService(AndroidUsageSync sync) : IStatsService
{
    public CollectionStatus GetStatus() => new(
        Supported: true,
        HasPermission: AndroidUsageReader.HasUsageAccess(),
        LastUpload: AndroidUsageSync.LastUpload,
        LastError: AndroidUsageSync.LastError,
        PendingBuckets: 0);

    public void OpenUsageAccessSettings()
    {
        var intent = new Intent(Settings.ActionUsageAccessSettings);
        intent.SetFlags(ActivityFlags.NewTask);
        Application.Context.StartActivity(intent);
    }

    public async Task<CollectionStatus> SyncNowAsync()
    {
        await sync.SyncAsync();
        return GetStatus();
    }
}
