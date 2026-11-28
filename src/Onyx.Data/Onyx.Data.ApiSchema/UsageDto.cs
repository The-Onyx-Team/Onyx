namespace Onyx.Data.ApiSchema;

/// <summary>Foreground time of one app on one device within one hourly UTC bucket.</summary>
public class UsageDto
{
    public int AppId { get; set; }
    public required string AppName { get; set; }
    public string? Category { get; set; }
    public TimeSpan TimeSpan { get; set; }

    /// <summary>Start of the hourly bucket (UTC).</summary>
    public DateTime Date { get; set; }

    public int DeviceId { get; set; }
    public required string DeviceName { get; set; }

    /// <summary>Website the time was spent on, for browsers on devices that record websites; otherwise null.
    /// A browser's time in one hour can be split over several rows, one per domain.</summary>
    public string? Domain { get; set; }

    /// <summary>How often the app was brought to the front in this hour, or, for a row with a
    /// <see cref="Domain"/>, how often that website was opened. Zero for data recorded before opens were counted.</summary>
    public int Opens { get; set; }
}

/// <summary>How often a device was unlocked within one hourly UTC bucket.</summary>
public record DeviceUnlocks(int DeviceId, string DeviceName, DateTime Hour, int Count);

/// <summary>
/// One entry of a usage upload. Collectors sum an app's foreground time into hourly UTC buckets; the server
/// stores one row per device, app, website and bucket. An upload replaces everything stored for each app and hour
/// it contains, so retries and re-sending the still-growing current hour are safe, but an upload must contain all
/// entries of an app's hour (all its websites), never just some of them.
/// </summary>
/// <param name="AppIdentifier">Platform-qualified stable key, e.g. <c>android:com.whatsapp</c>.</param>
/// <param name="AppName">Display name of the app.</param>
/// <param name="Category">Optional category reported by the platform.</param>
/// <param name="BucketStart">Start of the hour (UTC, minutes and seconds zero).</param>
/// <param name="ForegroundTime">Time in the foreground within that hour, at most one hour.</param>
/// <param name="Domain">For browsers: the website (domain only, see <see cref="WebDomain"/>) the time was spent
/// on. Null for time without a known website and for all other apps. A browser's hour is then sent as several
/// entries, one per domain, which together add up to the browser's time.</param>
/// <param name="Opens">How often the app came to the front in that hour; with a <paramref name="Domain"/>, how
/// often the website was opened. For <see cref="DeviceActivity.Identifier"/> it counts device unlocks. An entry
/// may have opens but no time (an app opened only for a moment).</param>
public record UsageEntryDto(
    string AppIdentifier,
    string AppName,
    string? Category,
    DateTime BucketStart,
    TimeSpan ForegroundTime,
    string? Domain = null,
    int Opens = 0);

/// <summary>
/// Device-wide activity is sent like an app with this reserved identifier: its entries carry no time, and their
/// <see cref="UsageEntryDto.Opens"/> count how often the device was unlocked. It never gets app information and
/// is not shown as an app.
/// </summary>
public static class DeviceActivity
{
    public const string Identifier = "onyx:device";
    public const string Name = "Device unlocks";
}

public record UsageUploadDto(List<UsageEntryDto> Entries);

/// <param name="Stored">Number of buckets written.</param>
/// <param name="AppsMissingIcon">Identifiers of uploaded apps the server has no icon for yet; the client
/// should send them with <c>PUT /api/app/icon</c>.</param>
public record UsageUploadResultDto(int Stored, List<string> AppsMissingIcon);
