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
}

/// <summary>
/// One entry of a usage upload. Collectors sum an app's foreground time into hourly UTC buckets; the server
/// stores one row per device, app and bucket, so uploading the same bucket again replaces the previous value.
/// That makes retries and overlapping uploads safe.
/// </summary>
/// <param name="AppIdentifier">Platform-qualified stable key, e.g. <c>android:com.whatsapp</c>.</param>
/// <param name="AppName">Display name of the app.</param>
/// <param name="Category">Optional category reported by the platform.</param>
/// <param name="BucketStart">Start of the hour (UTC, minutes and seconds zero).</param>
/// <param name="ForegroundTime">Time in the foreground within that hour, at most one hour.</param>
public record UsageEntryDto(
    string AppIdentifier,
    string AppName,
    string? Category,
    DateTime BucketStart,
    TimeSpan ForegroundTime);

public record UsageUploadDto(List<UsageEntryDto> Entries);

/// <param name="Stored">Number of buckets written.</param>
/// <param name="AppsMissingIcon">Identifiers of uploaded apps the server has no icon for yet; the client
/// should send them with <c>PUT /api/app/icon</c>.</param>
public record UsageUploadResultDto(int Stored, List<string> AppsMissingIcon);
