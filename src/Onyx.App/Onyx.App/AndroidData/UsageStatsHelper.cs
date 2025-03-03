using Android.App.Usage;
using Android.Content;

namespace Onyx.App.AndroidData;

public class UsageStatsHelper : IUsageStatsHelper
{
    public List<string> GetUsageStatsString(Context context)
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
    
    public List<UsageStats>? GetUsageStatsRaw(Context context)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<UsageStats>();
        }
        
        return usageStatsList.Select(u => new UsageStats(u))
            .OrderByDescending(u => u.TotalTimeInForeground)
            .ToList();
    }

    public List<UsageStats>? GetUsageStatsLastYear(Context context)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (365L * 24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Yearly, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<UsageStats>();
        }
        
        return usageStatsList.Select(u => new UsageStats(u))
            .OrderByDescending(u => u.TotalTimeInForeground)
            .ToList();
    }
    
    public List<UsageStats>? GetUsageStatsLastMonth(Context context)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (30L * 24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Monthly, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<UsageStats>();
        }
        
        return usageStatsList.Select(u => new UsageStats(u))
            .OrderByDescending(u => u.TotalTimeInForeground)
            .ToList();
    }
    
    public List<UsageStats>? GetUsageStatsLastWeek(Context context)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (7 * 24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Weekly, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<UsageStats>();
        }
        
        return usageStatsList.Select(u => new UsageStats(u))
            .OrderByDescending(u => u.TotalTimeInForeground)
            .ToList();
    }
    
    public List<UsageStats>? GetUsageStatsLastDay(Context context)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        long endTime = Java.Lang.JavaSystem.CurrentTimeMillis();
        long startTime = endTime - (24 * 60 * 60 * 1000);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<UsageStats>();
        }
        
        return usageStatsList.Select(u => new UsageStats(u))
            .OrderByDescending(u => u.TotalTimeInForeground)
            .ToList();
    }

    public List<UsageStats>? GetUsageStatsTimeInterval(Context context, long startTime, long endTime)
    {
        var usageStatsManager = (UsageStatsManager)context.GetSystemService(Context.UsageStatsService);
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return new List<UsageStats>();
        }
        
        return usageStatsList.Select(u => new UsageStats(u))
            .OrderByDescending(u => u.TotalTimeInForeground)
            .ToList();
    }
}