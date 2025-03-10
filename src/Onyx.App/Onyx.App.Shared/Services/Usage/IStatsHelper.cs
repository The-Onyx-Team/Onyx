namespace Onyx.App.Shared.Services.Usage;

public interface IStatsHelper
{
    List<Stats>? GetUsageStatsTimeIntervalMilliseconds(long startTime, long endTime);
}