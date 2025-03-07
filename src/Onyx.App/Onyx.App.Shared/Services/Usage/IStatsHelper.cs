namespace Onyx.App.Shared.Services.Usage;

public interface IStatsHelper
{
    List<Stats>? GetUsageStatsTimeInterval(long startTime, long endTime);
}