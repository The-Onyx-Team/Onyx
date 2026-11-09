using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>Usage data of the signed-in user.</summary>
public interface IUsageDataService
{
    /// <summary>Hourly buckets that start within [<paramref name="startTime"/>, <paramref name="endTime"/>).
    /// Times are converted to UTC.</summary>
    Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime, int? deviceId = null);

    /// <summary>Stores (or replaces) the given hourly buckets for one of the user's devices.</summary>
    /// <exception cref="KeyNotFoundException">The device does not exist or belongs to another user.</exception>
    /// <exception cref="ArgumentException">An entry is malformed (not an hour bucket, bad duration, ...).</exception>
    Task<UsageUploadResultDto> UploadUsageAsync(int deviceId, IReadOnlyList<UsageEntryDto> entries);
}
