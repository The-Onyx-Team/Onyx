using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.UsageData;

public class StatsHelper : IStatsHelper
{
    public List<Stats>? GetUsageStatsTimeIntervalMilliseconds(long startTime, long endTime)
    {
        throw new NotImplementedException();
    }
    
    public string GetAppNameFromPackage(string packageName)
    {
        throw new NotImplementedException();
    }
    
    public string GetCategoryFromPackage(string packageName)
    {
        throw new NotImplementedException();
    }
    
    public byte[] GetIconFromPackage(string packageName)
    {
        throw new NotImplementedException();
    }
}