using Android.App.Usage;
using Android.Content;

namespace Onyx.App.AndroidData;

public class UsageStatsHelper
{
    public static List<string> GetUsageStats(Context context)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<string> { "No data available. Ensure permissions are granted." };
        }
        
        var sortedUsageStatsList = usageStatsList.OrderByDescending(u => u.TotalTimeInForeground).ToList();
        return sortedUsageStatsList.Select(u => $"{u.PackageName}: {TimeSpan.FromMilliseconds(u.TotalTimeInForeground)}").ToList();
    }
}