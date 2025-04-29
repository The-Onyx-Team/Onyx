using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

public interface IUsageDataService
{
    Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime);
    Task<List<UsageDto>> GetUsageDataForDeviceAsync(DateTime startTime, DateTime endTime, int deviceId);
    Task<List<UsageDto>> GetUsageDataForUserAsync(DateTime startTime, DateTime endTime, string userId);
    Task<bool> UploadUsageData(List<UsageDto> usageData);
}