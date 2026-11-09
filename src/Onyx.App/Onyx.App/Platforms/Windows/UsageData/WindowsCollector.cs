using Microsoft.Extensions.Logging;
using Onyx.App.Services.Collection;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;

namespace Onyx.App.UsageData;

/// <summary>
/// Desktop collection on Windows: samples the foreground app every few seconds into a persisted queue and
/// uploads it every five minutes. Runs for as long as the app runs (also while hidden in the tray).
/// </summary>
public sealed class WindowsCollector : IStatsService, IDisposable
{
    private static readonly TimeSpan s_SyncInterval = TimeSpan.FromMinutes(5);

    private readonly PendingUsageStore m_Store;
    private readonly ForegroundTracker m_Tracker;
    private readonly UsageSyncService m_Sync;
    private readonly CancellationTokenSource m_Stop = new();
    private Task? m_Running;

    public WindowsCollector(OnyxApiClient client, ILoggerFactory loggerFactory)
    {
        var sources = new WindowsSources();
        m_Store = new PendingUsageStore(Path.Combine(FileSystem.AppDataDirectory, "pending-usage.json"),
            loggerFactory.CreateLogger<PendingUsageStore>());
        m_Tracker = new ForegroundTracker(sources, sources, m_Store, TimeProvider.System,
            logger: loggerFactory.CreateLogger<ForegroundTracker>());
        m_Sync = new UsageSyncService(client, m_Store, sources, TimeProvider.System, DeviceIdentity.Name,
            DeviceIdentity.Platform, loggerFactory.CreateLogger<UsageSyncService>());
    }

    public void Start() => m_Running ??= Task.WhenAll(
        Task.Run(() => m_Tracker.RunAsync(m_Stop.Token)),
        Task.Run(() => m_Sync.RunAsync(s_SyncInterval, m_Stop.Token)));

    /// <summary>Stops sampling and makes a last upload attempt; called when the app quits.</summary>
    public async Task StopAsync()
    {
        await m_Stop.CancelAsync();
        if (m_Running is not null)
            await m_Running;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await m_Sync.SyncOnceAsync(timeout.Token);
    }

    public CollectionStatus GetStatus() =>
        new(Supported: true, HasPermission: true, m_Sync.LastSuccess, m_Sync.LastError, m_Store.Count);

    public void OpenUsageAccessSettings()
    {
    }

    public async Task<CollectionStatus> SyncNowAsync()
    {
        await m_Sync.SyncOnceAsync(CancellationToken.None);
        return GetStatus();
    }

    public void Dispose() => m_Stop.Dispose();
}
