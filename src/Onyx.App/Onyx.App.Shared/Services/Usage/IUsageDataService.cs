using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>Usage data of the signed-in user, decrypted on this device.</summary>
public interface IUsageDataService
{
    /// <summary>Hourly buckets that start within [<paramref name="startTime"/>, <paramref name="endTime"/>), with
    /// the user's own app names and categories. Apps with the same name share one <see cref="UsageDto.AppId"/>.
    /// Hidden apps are left out.</summary>
    Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime, int? deviceId = null);

    /// <summary>The same usage, and how often the devices were unlocked in each hour (only devices that record
    /// unlocks, i.e. Android).</summary>
    Task<(List<UsageDto> Usage, List<DeviceUnlocks> Unlocks)> GetUsageAndUnlocksAsync(DateTime startTime,
        DateTime endTime, int? deviceId = null);
}
