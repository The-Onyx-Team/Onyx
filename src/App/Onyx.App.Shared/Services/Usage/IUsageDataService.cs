namespace Onyx.App.Shared.Services.Usage;

public interface IUsageDataService
{
    Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime);
    Task<List<UsageDto>> GetUsageDataForDeviceAsync(DateTime startTime, DateTime endTime, int deviceId);
    Task<bool> UploadUsageData(List<UsageDto> usageData, int deviceId);
}