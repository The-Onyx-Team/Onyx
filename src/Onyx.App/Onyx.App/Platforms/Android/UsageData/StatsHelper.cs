using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Onyx.App.Shared.Services.Usage;
using Application = Android.App.Application;

namespace Onyx.App.UsageData;

public class StatsHelper : IStatsHelper
{
    public List<Stats>? GetUsageStatsTimeInterval(long startTime, long endTime)
    {
        var usageStatsManager = (UsageStatsManager)Application.Context.GetSystemService(Context.UsageStatsService)!;
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<Stats>();
        }
        
        return usageStatsList
            .Select(u => new Stats()
            {
                Name = GetAppNameFromPackage(u.PackageName),
                TimeInForeground = TimeSpan.FromMilliseconds(u.TotalTimeInForeground),
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible),
                Category = GetCategoryFromPackage(u.PackageName)
            })
            .Where(u => u.TimeInForeground != TimeSpan.Zero && u.TimeVisible != TimeSpan.Zero)
            .OrderByDescending(u => u.TimeInForeground)
            .ToList();
    }
    
    public string GetAppNameFromPackage(string packageName)
    {
        try
        {
            var packageManager = Application.Context.PackageManager;
            var applicationInfo = packageManager?.GetApplicationInfo(packageName, 0);
            return (applicationInfo != null
                ? packageManager.GetApplicationLabel(applicationInfo)
                : packageName) ?? string.Empty;
        }
        catch (Exception e)
        {
            return packageName;
        }
    }
    
    public string GetCategoryFromPackage(string packageName)
    {
        try
        {
            var packageManager = Application.Context.PackageManager;
            var applicationInfo = packageManager?.GetApplicationInfo(packageName, 0);
            return (applicationInfo != null
                ? applicationInfo.Category.ToString()
                : packageName) ?? string.Empty;
        }
        catch (Exception e)
        {
            return packageName;
        }
    }
}