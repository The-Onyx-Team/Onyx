using Microsoft.Extensions.Logging;
using Onyx.App.Services.Collection;
using Onyx.Collector.Core;

namespace Onyx.App.UsageData;

/// <summary>
/// One sync: read buckets since the last confirmed hour and upload them. The server replaces buckets it already
/// has, so the current hour is simply sent again next time.
/// </summary>
public class AndroidUsageSync(OnyxApiClient client, AndroidUsageReader reader, ILogger<AndroidUsageSync> logger)
{
    private const string SyncedUntilKey = "onyx_synced_until";
    private const string LastUploadKey = "onyx_last_upload";
    private const string LastErrorKey = "onyx_last_error";

    /// <summary>First sync after install: upload what Android still has (it keeps about a week).</summary>
    private static readonly TimeSpan s_FirstSyncBackfill = TimeSpan.FromDays(7);

    private readonly SemaphoreSlim m_Lock = new(1, 1);

    public static DateTimeOffset? LastUpload =>
        Preferences.Default.Get(LastUploadKey, 0L) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public static string? LastError => Preferences.Default.Get(LastErrorKey, (string?)null);

    /// <returns>False when the sync should be retried later.</returns>
    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        if (!AndroidUsageReader.HasUsageAccess())
        {
            SetError("Usage access has not been granted");
            return true; // retrying won't help until the user grants it
        }

        if (!await client.IsSignedInAsync())
        {
            SetError("Not signed in");
            return true;
        }

        await m_Lock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var currentHour = UsageBuckets.HourOf(now);
            var stored = Preferences.Default.Get(SyncedUntilKey, 0L);
            var from = stored > 0
                ? new DateTime(stored, DateTimeKind.Utc)
                : UsageBuckets.HourOf(now - s_FirstSyncBackfill);

            var entries = reader.ReadBuckets(from, now);
            if (entries.Count > 0)
            {
                // Encrypted with the key the app stores after the passphrase was entered; without it this throws
                // VaultLockedException and the data stays in Android's history until the user unlocks.
                await client.UploadUsageAsync(entries, DeviceIdentity.Name, DeviceIdentity.Platform, reader, ct);
            }

            Preferences.Default.Set(SyncedUntilKey, currentHour.Ticks);
            Preferences.Default.Set(LastUploadKey, now.UtcTicks);
            Preferences.Default.Remove(LastErrorKey);
            logger.LogInformation("Uploaded {Count} usage buckets", entries.Count);
            return true;
        }
        catch (NotSignedInException e)
        {
            SetError(e.Message);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            SetError(e.Message);
            return false;
        }
        finally
        {
            m_Lock.Release();
        }
    }

    private static void SetError(string message) => Preferences.Default.Set(LastErrorKey, message);
}
