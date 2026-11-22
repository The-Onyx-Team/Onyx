using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Services.Reports;

/// <summary>Weekly reports of the signed-in user, for the web UI and <c>/api/reports</c>.</summary>
public class ReportService(IUsageDataService usage, ReportChartRenderer renderer, TimeProvider timeProvider)
    : IReportService
{
    public async Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, TimeZoneInfo zone)
    {
        weekStart = WeeklyReportBuilder.WeekStartOf(weekStart);
        var (start, end) = WeeklyReportBuilder.UtcRange(weekStart, zone);
        var rows = await usage.GetUsageDataAsync(start, end);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
        var (report, input) = WeeklyReportBuilder.Build(rows, weekStart, zone, today);

        if (report.Total == TimeSpan.Zero && report.Trend.All(t => t.Time == TimeSpan.Zero))
            return report;

        var (charts, error) = await renderer.RenderAsync(input);
        return report with { Charts = charts, ChartError = error };
    }
}
