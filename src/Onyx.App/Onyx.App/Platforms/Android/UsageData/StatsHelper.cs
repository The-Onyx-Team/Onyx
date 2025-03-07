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
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible)
            })
            .Where(u => u.TimeInForeground != TimeSpan.Zero && u.TimeVisible != TimeSpan.Zero)
            .OrderByDescending(u => u.TimeInForeground)
            .ToList();
    }
    
    public string GetAppNameFromPackage(string packageName)
    {
        try
        {
            var packageManager = Android.App.Application.Context.PackageManager;
            Console.WriteLine($"packageName: {packageName}");
            var applicationInfo = packageManager?.GetApplicationInfo(packageName, 0);
            return (applicationInfo != null
                ? packageManager?.GetApplicationLabel(applicationInfo)
                : packageName) ?? string.Empty; // Fallback: Package-Name zurückgeben
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to get app name from package: {e.Message}");
            return packageName; // Falls die App nicht gefunden wurde
        }
    }
}