using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>Builds the weekly report from decrypted usage and draws its charts (<see cref="ReportCharts"/>).</summary>
public sealed class ClientReportService(IUsageDataService usage, VaultService vault, TimeProvider time) : IReportService
{
    public async Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, TimeZoneInfo zone)
    {
        weekStart = WeeklyReportBuilder.WeekStartOf(weekStart);
        var (start, end) = WeeklyReportBuilder.UtcRange(weekStart, zone);
        var rows = await usage.GetUsageDataAsync(start, end);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var (report, input) = WeeklyReportBuilder.Build(rows, weekStart, zone, today);
        if (report.Total == TimeSpan.Zero && report.Trend.All(t => t.Time == TimeSpan.Zero))
            return report;

        var colors = (await vault.GetSettingsAsync()).Categories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Color, StringComparer.OrdinalIgnoreCase);
        return report with { Charts = ReportCharts.Render(input, colors) };
    }
}
