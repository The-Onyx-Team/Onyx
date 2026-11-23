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
            await client.UploadUsageAsync(snapshot, deviceName, platform, iconSource, cancellationToken);
            store.Acknowledge(snapshot, UsageBuckets.HourOf(timeProvider.GetUtcNow()));
            store.Save();

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
            // Includes a locked vault: the data stays queued until the user signs in or enters the passphrase.
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

    /// <summary>Picks up websites the user excluded in the dashboard. They are part of the encrypted settings,
    /// so the server can't filter them; a failed refresh keeps the list from the last refresh.</summary>
    private async Task RefreshExcludedDomainsAsync(BrowserActivity activity, CancellationToken ct)
    {
        try
        {
            if (await client.GetVaultSettingsAsync(ct) is { } settings)
                activity.SetExcludedDomains(settings.ExcludedDomains);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or NotSignedInException
                                      or System.Security.Cryptography.CryptographicException)
        {
            m_Logger.LogDebug("Could not refresh excluded websites: {Reason}", e.Message);
        }
    }
}
