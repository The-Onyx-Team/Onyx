using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Reports;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Documents;

/// <summary>Draws a chart on a PDF page: the chart's own units scaled into a box at (<c>x</c>, <c>y</c>).</summary>
public sealed class PdfChartCanvas(PdfWriter pdf, double x, double y, double scale) : IChartCanvas
{
    private (double, double) P(double cx, double cy) => (x + cx * scale, y + cy * scale);

    public void Rect(double rx, double ry, double width, double height, string fill, double radius = 0)
    {
        if (width <= 0 || height <= 0)
            return;
        var (px, py) = P(rx, ry);
        pdf.Rect(px, py, width * scale, height * scale, fill);
    }

    public void Line(double x1, double y1, double x2, double y2, string stroke, double width)
    {
        var (a, b) = P(x1, y1);
        var (c, d) = P(x2, y2);
        pdf.Line(a, b, c, d, stroke, width * scale);
    }

    public void Text(double tx, double ty, string text, double size, string fill, string anchor = "start",
        string weight = "400", double rotate = 0)
    {
        var bold = weight is "700" or "bold";
        var fontSize = size * scale;
        var shift = anchor switch
        {
            "middle" => PdfWriter.Width(text, fontSize, bold) / 2,
            "end" => PdfWriter.Width(text, fontSize, bold),
            _ => 0
        };
        var (px, py) = P(tx, ty);
        if (rotate == 0)
        {
            pdf.Text(px - shift, py, text, fontSize, bold, fill);
            return;
        }

        // Move the start back along the rotated baseline.
        var a = rotate * Math.PI / 180;
        pdf.TextRotated(px - shift * Math.Cos(a), py - shift * Math.Sin(a), text, fontSize, rotate, bold, fill);
    }

    public void Polygon(IReadOnlyList<(double X, double Y)> points, string fill, string? stroke = null,
        double strokeWidth = 0) =>
        pdf.Polygon(points.Select(p => P(p.X, p.Y)), fill, stroke, strokeWidth * scale);

    public void Polyline(IEnumerable<(double X, double Y)> points, string stroke, double width, double opacity = 1) =>
        pdf.Polyline(points.Select(p => P(p.X, p.Y)), stroke, width * scale);

    public void Circle(double cx, double cy, double r, string fill, string stroke, double strokeWidth)
    {
        var points = Enumerable.Range(0, 36)
            .Select(i => (cx + r * Math.Cos(i * Math.PI / 18), cy + r * Math.Sin(i * Math.PI / 18)))
            .ToList();
        Polygon(points, fill, strokeWidth > 0 ? stroke : null, strokeWidth);
    }
}

/// <summary>The weekly report as an A4 PDF with the same content and charts as the page (<see cref="ReportPdf"/>).</summary>
public static class WeeklyReportPdf
{
    public static ReportLayout Layout(WeeklyReportDto report, IReadOnlyDictionary<string, ChartDrawing> charts,
        string weekText)
    {
        var top = report.Apps.FirstOrDefault();
        var stats = report.Total == TimeSpan.Zero
            ? new List<ReportStat>()
            :
            [
                new("Screen time", UsageSummary.Format(report.Total), $"{ReportText.Change(report.Total, report.PreviousTotal)} vs. the week before"),
                new("Per day", UsageSummary.Format(report.DailyAverage), "on average"),
                new("Busiest day", report.BusiestDay ?? "–", UsageSummary.Format(report.BusiestDayTime)),
                new("Most used", top?.Name ?? "–", UsageSummary.Format(top?.Time ?? TimeSpan.Zero))
            ];

        List<ReportBlock> blocks = report.Total == TimeSpan.Zero
            ? [new TextBlock("No usage was recorded in this week.")]
            :
            [
                new ChartBlock("daily", "Every day", "Screen time per day, split by device."),
                new ChartBlock("apps", "Top apps", "This week compared with the week before."),
                new SideBySideBlock(new ChartBlock("categories", "Categories"),
                    new ChartBlock("trend", "Trend", $"Screen time per week, last {WeeklyReportDto.TrendWeeks} weeks.")),
                new TextBlock(ReportText.Trend(report)),
                new ChartBlock("heatmap", "When", "Minutes of use per hour of the day."),
                ..report.Devices.Count > 0 ? [new HeadingBlock("Devices")] : Array.Empty<ReportBlock>(),
                ..report.Devices.Select(d => new DeviceBlock($"{d.Name}: {UsageSummary.Format(d.Time)}",
                    $"({ReportText.Change(d.Time, d.PreviousTime)} vs. the week before)", $"device-{d.Id}"))
            ];

        return new ReportLayout("Weekly report", weekText, stats, blocks, charts, $"Onyx weekly report, {weekText}");
    }

    public static byte[] Render(WeeklyReportDto report, IReadOnlyDictionary<string, ChartDrawing> charts,
        string weekText, DateTime created) =>
        ReportPdf.Render(Layout(report, charts, weekText), created);
}

/// <summary>Sentences of the weekly report, shared by the page and the PDF.</summary>
public static class ReportText
{
    public static string Change(TimeSpan current, TimeSpan previous)
    {
        if (previous == TimeSpan.Zero)
            return current == TimeSpan.Zero ? "same" : "new";
        var change = (current - previous) / previous;
        return change switch
        {
            > 0.005 => $"+{change:P0}",
            < -0.005 => $"{change:P0}",
            _ => "about the same"
        };
    }

    public static string Trend(WeeklyReportDto report)
    {
        var earlier = report.Trend.Take(report.Trend.Count - 1).Where(t => t.Time > TimeSpan.Zero).ToList();
        if (earlier.Count == 0)
            return "There is no data from earlier weeks to compare with yet.";

        var average = TimeSpan.FromTicks((long)earlier.Average(t => t.Time.Ticks));
        var change = (report.Total - average) / average;
        var direction = change switch
        {
            > 0.1 => "more than",
            < -0.1 => "less than",
            _ => "about the same as"
        };
        return $"This week you used your devices {direction} your average of {UsageSummary.Format(average)} " +
               $"per week over the {earlier.Count} earlier week(s) with data" +
               (Math.Abs(change) > 0.1 ? $" ({(change > 0 ? "+" : "")}{change:P0})." : ".");
    }
}
