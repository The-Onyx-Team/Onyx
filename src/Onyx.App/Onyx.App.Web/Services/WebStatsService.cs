using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.Web.Services;

/// <summary>The browser dashboard does not collect usage; devices do.</summary>
public class WebStatsService : IStatsService
{
    private static readonly CollectionStatus s_NotSupported = new(false, false, null, null, 0);

    public CollectionStatus GetStatus() => s_NotSupported;

    public void OpenUsageAccessSettings()
    {
    }

    public Task<CollectionStatus> SyncNowAsync() => Task.FromResult(s_NotSupported);
}
