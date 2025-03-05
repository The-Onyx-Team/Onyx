using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Onyx.App.Shared.Services.Usage;
using Application = Android.App.Application;

namespace Onyx.App.UsageData;

public class StatsHelper : IStatsHelper
{
    private readonly string[] _notInNames = ["android", "google", "com", "apple", "samsung"];


    public List<Stats>? GetUsageStatsLastYear()
    {
        var usageStatsManager = (UsageStatsManager)Application.Context.GetSystemService(Context.UsageStatsService)!;
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (365L * 24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Yearly, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<Stats>();
        }
        
        return usageStatsList
            .Select(u => new Stats()
            {
                Name = u.PackageName == null ? string.Empty :
                    string.Join(" ", u.PackageName.Split('.')
                        .Where(s => !_notInNames.Contains(s))
                        .ToArray()).ToString(),
                TimeInForeground = TimeSpan.FromMilliseconds(u.TotalTimeInForeground),
#pragma warning disable CA1416
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible)
#pragma warning restore CA1416
            })
            .Where(u => u.TimeInForeground != TimeSpan.Zero && u.TimeVisible != TimeSpan.Zero)
            .OrderByDescending(u => u.TimeInForeground)
            .ToList();
    }
    
    public List<Stats>? GetUsageStatsLastMonth()
    {
        var usageStatsManager = (UsageStatsManager)Application.Context.GetSystemService(Context.UsageStatsService)!;
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (30L * 24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Monthly, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<Stats>();
        }
        
        return usageStatsList
            .Select(u => new Stats()
            {
                Name = u.PackageName?
                    .Split('.')
                    .Where(s => !_notInNames.Contains(s))
                    .ToString() ?? string.Empty,
                TimeInForeground = TimeSpan.FromMilliseconds(u.TotalTimeInForeground),
#pragma warning disable CA1416
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible)
#pragma warning restore CA1416
            })
            .Where(u => u.TimeInForeground != TimeSpan.Zero && u.TimeVisible != TimeSpan.Zero)
            .OrderByDescending(u => u.TimeInForeground)
            .ToList();
    }
    
    public List<Stats>? GetUsageStatsLastWeek()
    {
        var usageStatsManager = (UsageStatsManager)Application.Context.GetSystemService(Context.UsageStatsService)!;
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (7 * 24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Weekly, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<Stats>();
        }
        
        return usageStatsList
            .Select(u => new Stats()
            {
                Name = GetAppNameFromPackage(u.PackageName ?? string.Empty),
                TimeInForeground = TimeSpan.FromMilliseconds(u.TotalTimeInForeground),
#pragma warning disable CA1416
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible)
#pragma warning restore CA1416
            })
            .Where(u => u.TimeInForeground != TimeSpan.Zero && u.TimeVisible != TimeSpan.Zero)
            .OrderByDescending(u => u.TimeInForeground)
            .ToList();
    }
    
    public List<Stats>? GetUsageStatsLastDay()
    {
        var usageStatsManager = (UsageStatsManager)Application.Context.GetSystemService(Context.UsageStatsService)!;
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<Stats>();
        }
        
        return usageStatsList
            .Select(u => new Stats()
            {
                Name = u.PackageName?
                    .Split('.')
                    .Where(s => !_notInNames.Contains(s))
                    .ToString() ?? string.Empty,
                TimeInForeground = TimeSpan.FromMilliseconds(u.TotalTimeInForeground),
#pragma warning disable CA1416
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible)
#pragma warning restore CA1416
            })
            .Where(u => u.TimeInForeground != TimeSpan.Zero && u.TimeVisible != TimeSpan.Zero)
            .OrderByDescending(u => u.TimeInForeground)
            .ToList();
    }

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
#pragma warning disable CA1416
                TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible)
#pragma warning restore CA1416
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