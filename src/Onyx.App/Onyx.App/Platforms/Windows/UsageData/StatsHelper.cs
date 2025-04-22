using Onyx.App.Platforms.Windows.UsageData;
using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.UsageData;

public class StatsHelper(DataCollector dataCollector) : IStatsHelper
{
    public List<Stats>? GetUsageStatsTimeIntervalMilliseconds(long startTime, long endTime)
    {
        return dataCollector.Stats;
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