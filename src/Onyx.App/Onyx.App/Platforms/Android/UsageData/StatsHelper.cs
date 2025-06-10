using Android.App.Usage;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Graphics;
using Onyx.App.Shared.Services.Usage;
using Application = Android.App.Application;

namespace Onyx.App.UsageData;

public class StatsHelper : IStatsHelper
{
    public List<Stats> GetUsageStatsTimeIntervalMilliseconds(long startTime, long endTime)
    {
        var usageStatsManager = (UsageStatsManager)Application.Context.GetSystemService(Context.UsageStatsService)!;
        
        var usageStatsList = usageStatsManager.QueryUsageStats(UsageStatsInterval.Daily, startTime, endTime);
        if (usageStatsList == null || !usageStatsList.Any())
        {
            return [];
        }
        
        return usageStatsList
            .Select(u =>
            {
                if (u.PackageName != null)
                    return new Stats()
                    {
                        Name = GetAppNameFromPackage(u.PackageName),
                        TimeInForeground = TimeSpan.FromMilliseconds(u.TotalTimeInForeground),
#pragma warning disable CA1416
                        TimeVisible = TimeSpan.FromMilliseconds(u.TotalTimeVisible),
#pragma warning restore CA1416
                        IntervalStart = DateTimeOffset.FromUnixTimeMilliseconds(u.FirstTimeStamp).DateTime,
                        IntervalEnd = DateTimeOffset.FromUnixTimeMilliseconds(u.LastTimeStamp).DateTime,
                        LastTimeUsed =  DateTimeOffset.FromUnixTimeMilliseconds(u.LastTimeUsed).DateTime,
                        Category = GetCategoryFromPackage(u.PackageName),
                        Icon = GetIconFromPackage(u.PackageName)
                    };
                return new Stats() {Name = "", TimeInForeground = TimeSpan.FromMilliseconds(0), Category = "Undefined", Icon = []};
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
                ? packageManager?.GetApplicationLabel(applicationInfo)
                : packageName) ?? string.Empty;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
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
#pragma warning disable CA1416
                ? applicationInfo.Category.ToString()
#pragma warning restore CA1416
                : packageName);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return packageName;
        }
    }
    
    public byte[] GetIconFromPackage(string packageName)
    {
        try
        {
            var packageManager = Application.Context.PackageManager;
            var applicationInfo = packageManager?.GetApplicationInfo(packageName, 0);
            if (applicationInfo != null)
            {
                var icon = applicationInfo.LoadIcon(packageManager);
                if (icon is BitmapDrawable { Bitmap: not null } bitmapDrawable) return BitmapToByteArray(bitmapDrawable.Bitmap);
                if (icon != null)
                {
                    if (Bitmap.Config.Argb8888 != null)
                    {
                        var bitmap = Bitmap.CreateBitmap(icon.IntrinsicWidth, icon.IntrinsicHeight, Bitmap.Config.Argb8888);
                        var canvas = new Canvas(bitmap);
                        icon.SetBounds(0, 0, canvas.Width, canvas.Height);
                        icon.Draw(canvas);
                        return BitmapToByteArray(bitmap);
                    }
                }
            }
            return [];
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return [];
        }
    }
    
    private static byte[] BitmapToByteArray(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        if (Bitmap.CompressFormat.Png != null) bitmap.Compress(Bitmap.CompressFormat.Png, 100, stream);
        return stream.ToArray();
    }
}