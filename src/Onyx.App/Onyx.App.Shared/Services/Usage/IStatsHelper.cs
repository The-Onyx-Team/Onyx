namespace Onyx.App.Shared.Services.Usage;

public interface IStatsHelper
{
    List<Stats>? GetUsageStatsLastDay();
    List<Stats>? GetUsageStatsLastWeek();
    List<Stats>? GetUsageStatsLastMonth();
    List<Stats>? GetUsageStatsLastYear();
    List<Stats>? GetUsageStatsTimeInterval(long startTime, long endTime);
}