namespace Onyx.App.Shared.Services.Usage;

/// <summary>State of usage collection on the device the UI runs on.</summary>
/// <param name="Supported">False in the browser: the web dashboard does not collect.</param>
/// <param name="HasPermission">Android: usage access granted. Always true where no permission is needed.</param>
/// <param name="PendingBuckets">Hourly buckets collected but not uploaded yet (desktop collectors).</param>
public record CollectionStatus(
    bool Supported,
    bool HasPermission,
    DateTimeOffset? LastUpload,
    string? LastError,
    int PendingBuckets);

/// <summary>Usage collection on this device. Each host implements it for its platform.</summary>
public interface IStatsService
{
    CollectionStatus GetStatus();

    /// <summary>Android: opens the system screen where usage access is granted. No-op elsewhere.</summary>
    void OpenUsageAccessSettings();

    /// <summary>Uploads what has been collected so far, now.</summary>
    Task<CollectionStatus> SyncNowAsync();
}
