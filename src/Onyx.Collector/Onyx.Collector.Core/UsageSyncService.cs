using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onyx.Collector.Core.Browser;

namespace Onyx.Collector.Core;

/// <summary>Uploads a <see cref="PendingUsageStore"/> periodically (desktop collectors).</summary>
public sealed class UsageSyncService(
    OnyxApiClient client,
    PendingUsageStore store,
    IAppIconSource? iconSource,
    TimeProvider timeProvider,
    string deviceName,
    string platform,
    ILogger? logger = null,
    BrowserActivity? browserActivity = null)
{
    private readonly ILogger m_Logger = logger ?? NullLogger.Instance;
    private readonly HashSet<string> m_IconsSent = new();

    public DateTimeOffset? LastSuccess { get; private set; }
    public string? LastError { get; private set; }

    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            await SyncOnceAsync(cancellationToken);
        } while (await WaitAsync(timer, cancellationToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <returns>True when everything pending was uploaded.</returns>
    public async Task<bool> SyncOnceAsync(CancellationToken cancellationToken)
    {
        if (browserActivity is not null)
            await RefreshExcludedDomainsAsync(browserActivity, cancellationToken);

        var snapshot = store.Snapshot();
        if (snapshot.Count == 0)
            return true;

        try
        {
            var missingIcons = await client.UploadUsageAsync(snapshot, deviceName, platform, cancellationToken);
            store.Acknowledge(snapshot, UsageBuckets.HourOf(timeProvider.GetUtcNow()));
            store.Save();

            await UploadIconsAsync(missingIcons, cancellationToken);

            LastSuccess = timeProvider.GetUtcNow();
            LastError = null;
            m_Logger.LogInformation("Uploaded {Count} usage buckets", snapshot.Count);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (NotSignedInException e)
        {
            LastError = e.Message;
            m_Logger.LogWarning("Not uploading: {Reason}", e.Message);
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Offline or server down: keep the data and try again next time.
            LastError = e.Message;
            m_Logger.LogWarning("Upload failed, will retry: {Reason}", e.Message);
            return false;
        }
    }

    /// <summary>Picks up websites the user excluded in the dashboard. The server drops them as well, so a failed
    /// refresh only means they are filtered there instead of here.</summary>
    private async Task RefreshExcludedDomainsAsync(BrowserActivity activity, CancellationToken ct)
    {
        try
        {
            activity.SetExcludedDomains(await client.GetWebsiteExclusionsAsync(ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or NotSignedInException)
        {
            m_Logger.LogDebug("Could not refresh excluded websites: {Reason}", e.Message);
        }
    }

    private async Task UploadIconsAsync(IEnumerable<string> appIdentifiers, CancellationToken ct)
    {
        if (iconSource is null)
            return;

        foreach (var app in appIdentifiers.Where(a => !m_IconsSent.Contains(a)))
        {
            m_IconsSent.Add(app);
            try
            {
                if (iconSource.GetIconPng(app) is { Length: > 0 } png)
                    await client.UploadIconAsync(app, png, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                m_Logger.LogDebug(e, "Could not upload icon for {App}", app);
            }
        }
    }
}
