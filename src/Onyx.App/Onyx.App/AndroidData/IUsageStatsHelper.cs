using Android.App.Usage;
using Android.Content;

namespace Onyx.App.AndroidData;

public interface IUsageStatsHelper
{
    List<string>? GetUsageStatsString(Context context);
    List<UsageStats>? GetUsageStatsRaw(Context context);
    List<UsageStats>? GetUsageStatsLastDay(Context context);
    List<UsageStats>? GetUsageStatsLastWeek(Context context);
    List<UsageStats>? GetUsageStatsLastMonth(Context context);
    List<UsageStats>? GetUsageStatsLastYear(Context context);
    List<UsageStats>? GetUsageStatsTimeInterval(Context context, long startTime, long endTime);
}