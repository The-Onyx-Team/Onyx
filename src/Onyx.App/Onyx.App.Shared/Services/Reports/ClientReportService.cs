using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Documents;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>Builds the weekly report from decrypted usage and draws its charts (<see cref="ReportCharts"/>), as SVG
/// for the page and as PDF for the download.</summary>
public sealed class ClientReportService(IUsageDataService usage, VaultService vault, TimeProvider time) : IReportService
{
    // The last report built, so the PDF of the report on screen doesn't load and decrypt everything again.
    private (DateOnly Week, string Zone, WeeklyReportDto Report, Dictionary<string, ChartDrawing> Charts)? m_Last;

    public async Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, TimeZoneInfo zone) =>
        (await BuildAsync(weekStart, zone)).Report;

    public async Task<byte[]> GetWeeklyReportPdfAsync(DateOnly weekStart, TimeZoneInfo zone)
    {
        weekStart = WeeklyReportBuilder.WeekStartOf(weekStart);
        var (report, charts) = m_Last is { } last && last.Week == weekStart && last.Zone == zone.Id
            ? (last.Report, last.Charts)
            : await BuildAsync(weekStart, zone);
        return WeeklyReportPdf.Render(report, charts, Weeks.LongLabel(weekStart), time.GetUtcNow().UtcDateTime);
    }

    private async Task<(WeeklyReportDto Report, Dictionary<string, ChartDrawing> Charts)> BuildAsync(
        DateOnly weekStart, TimeZoneInfo zone)
    {
        weekStart = WeeklyReportBuilder.WeekStartOf(weekStart);
        var (start, end) = WeeklyReportBuilder.UtcRange(weekStart, zone);
        var rows = await usage.GetUsageDataAsync(start, end);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var (report, input) = WeeklyReportBuilder.Build(rows, weekStart, zone, today);
        var charts = new Dictionary<string, ChartDrawing>();
        if (report.Total != TimeSpan.Zero || report.Trend.Any(t => t.Time != TimeSpan.Zero))
        {
            var colors = (await vault.GetSettingsAsync()).Categories
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Color, StringComparer.OrdinalIgnoreCase);
            charts = ReportCharts.Build(input, colors);
            report = report with { Charts = charts.ToDictionary(c => c.Key, c => c.Value.ToSvg()) };
        }

        m_Last = (weekStart, zone.Id, report, charts);
        return (report, charts);
    }
}
