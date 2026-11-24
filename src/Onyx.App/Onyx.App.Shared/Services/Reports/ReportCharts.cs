using System.Globalization;
using System.Net;
using System.Text;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>
/// Draws the weekly report's charts on the device: the data is end-to-end encrypted, so the server can't draw them.
/// Each chart draws on an <see cref="IChartCanvas"/>: SVG for the page, PDF for the download
/// (<see cref="Documents.WeeklyReportPdf"/>). The look: onyx ink on pearl paper with sardonyx accents.
/// </summary>
public static class ReportCharts
{
    public const string Paper = "#F7F5F1";
    public const string Ink = "#131518";
    public const string Muted = "#5B616B";
    public const string Grid = "#DAD6CE";
    public const string Accent = "#B4532A";

    /// <summary>Series colours: sardonyx and banded onyx first, then companions that stay apart on the paper.</summary>
    public static readonly string[] Series =
    [
        "#B4532A", "#3B4048", "#C9A27E", "#6F8594", "#8E9A7B", "#7A4A6B", "#2F6F73", "#D98E5F", "#9AA0A8", "#5C2E1B"
    ];

    private const string Font = "font-family=\"Inter, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif\"";

    /// <summary>The charts as SVG, by key (see <see cref="Build"/>).</summary>
    /// <param name="categoryColors">The user's category colours; others get series colours.</param>
    public static Dictionary<string, string> Render(ChartInput input, IReadOnlyDictionary<string, string>? categoryColors = null) =>
        Build(input, categoryColors).ToDictionary(c => c.Key, c => c.Value.ToSvg());

    /// <summary>The charts by key: <c>daily</c>, <c>apps</c>, <c>categories</c>, <c>trend</c>, <c>heatmap</c> and
    /// <c>device-{id}</c> per device.</summary>
    public static Dictionary<string, ChartDrawing> Build(ChartInput input,
        IReadOnlyDictionary<string, string>? categoryColors = null)
    {
        var charts = new Dictionary<string, ChartDrawing> { ["daily"] = Daily(input.Days, input.DailyByDevice) };
        if (input.Apps.Count > 0)
            charts["apps"] = Apps(input.Apps);
        if (input.Categories.Count > 0)
            charts["categories"] = Categories(input.Categories, categoryColors ?? new Dictionary<string, string>());
        charts["trend"] = Trend(input.Trend);
        charts["heatmap"] = Heatmap(input.Days, input.Heatmap);
        foreach (var device in input.Devices)
            charts[device.Key] = Device(input.DaysShort.Length == 7 ? input.DaysShort : input.Days, device.Daily, device.Apps);
        return charts;
    }

    public static string HoursLabel(double hours)
    {
        var minutes = (int)Math.Round(hours * 60);
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00}";
    }

    // Charts

    private static ChartDrawing Daily(string[] days, Dictionary<string, double[]> byDevice)
    {
        const double width = 800, height = 340, left = 56, right = 16, top = 34, bottom = 40;
        var totals = Enumerable.Range(0, days.Length).Select(i => byDevice.Values.Sum(v => v[i])).ToArray();
        var (max, step) = Scale(totals.DefaultIfEmpty(0).Max() * 1.15);
        var plotHeight = height - top - bottom;
        var plotWidth = width - left - right;
        double Y(double value) => top + plotHeight - value / max * plotHeight;

        return new ChartDrawing(width, height, canvas =>
        {
            YAxis(canvas, left, width - right, top, plotHeight, max, step, "hours");
            var column = plotWidth / Math.Max(days.Length, 1);
            var bar = column * 0.62;
            for (var i = 0; i < days.Length; i++)
            {
                var x = left + column * i + (column - bar) / 2;
                double bottomValue = 0;
                var series = 0;
                foreach (var (_, values) in byDevice)
                {
                    var value = values[i];
                    if (value > 0)
                        canvas.Rect(x, Y(bottomValue + value), bar, Y(bottomValue) - Y(bottomValue + value),
                            Series[series % Series.Length], radius: 2);
                    bottomValue += value;
                    series++;
                }

                // Long ranges: values only on hover-free paper would crowd, so they are left out, and only every
                // few days are labelled.
                if (totals[i] > 0 && days.Length <= 14)
                    canvas.Text(x + bar / 2, Y(totals[i]) - 6, HoursLabel(totals[i]), 11, Muted, anchor: "middle");
                if (i % LabelEvery(days.Length) == 0)
                    canvas.Text(left + column * i + column / 2, height - bottom + 18, days[i], 11, Muted, anchor: "middle");
            }

            if (byDevice.Count > 1)
                Legend(canvas, left, 16, byDevice.Keys.Select((k, i) => (k, Series[i % Series.Length])));
        });
    }

    private static ChartDrawing Apps(List<ChartApp> apps)
    {
        const double width = 800, right = 70, top = 16, row = 30;
        var nameWidth = Math.Clamp(apps.Max(a => Shorten(a.Name, 28).Length) * 7.2 + 16, 80, 230);
        var left = nameWidth;
        var height = top + row * apps.Count + 44;
        var (max, step) = Scale(apps.Max(a => Math.Max(a.Hours, a.PreviousHours)) * 1.1);
        var plotWidth = width - left - right;
        double X(double value) => left + value / max * plotWidth;

        return new ChartDrawing(width, height, canvas =>
        {
            for (var tick = 0.0; tick <= max + 1e-9; tick += step)
            {
                canvas.Line(X(tick), top, X(tick), top + row * apps.Count, Grid, 0.6);
                canvas.Text(X(tick), top + row * apps.Count + 16, Number(tick), 10, Muted, anchor: "middle");
            }

            for (var i = 0; i < apps.Count; i++)
            {
                var y = top + row * i;
                var app = apps[i];
                canvas.Text(left - 10, y + row / 2 + 4, Shorten(app.Name, 28), 12, Ink, anchor: "end");
                if (app.PreviousHours > 0)
                    canvas.Rect(left, y + row / 2 + 1, X(app.PreviousHours) - left, 8, Grid, radius: 2);
                if (app.Hours > 0)
                    canvas.Rect(left, y + row / 2 - 11, X(app.Hours) - left, 11, Accent, radius: 2);
                canvas.Text(X(app.Hours) + 6, y + row / 2 - 2, HoursLabel(app.Hours), 10.5, Ink);
            }

            canvas.Text(left + plotWidth / 2, height - 6, "hours", 10.5, Muted, anchor: "middle");
            Legend(canvas, width - 250, height - 10, [("this week", Accent), ("week before", Grid)]);
        });
    }

    private static ChartDrawing Categories(List<ChartApp> categories, IReadOnlyDictionary<string, string> colors)
    {
        const double width = 420, cx = 210, cy = 175, outer = 150, inner = 94;
        var rows = (categories.Count + 1) / 2;
        var height = 350 + rows * 20 + 10;
        var total = categories.Sum(c => c.Hours);
        var used = new HashSet<string>(colors.Values, StringComparer.OrdinalIgnoreCase);
        var fallback = Series.Where(s => !used.Contains(s)).Concat(Series).GetEnumerator();
        var fills = categories.Select(c =>
        {
            if (colors.TryGetValue(c.Name, out var color))
                return color;
            fallback.MoveNext();
            return fallback.Current;
        }).ToList();

        return new ChartDrawing(width, height, canvas =>
        {
            var angle = -Math.PI / 2;
            for (var i = 0; i < categories.Count; i++)
            {
                var sweep = total > 0 ? categories[i].Hours / total * 2 * Math.PI : 0;
                if (sweep <= 0)
                    continue;
                // A single category is a whole ring: no gap line where it starts.
                var whole = sweep >= 2 * Math.PI - 1e-6;
                canvas.Polygon(DonutSlice(cx, cy, outer, inner, angle, angle + sweep), fills[i],
                    stroke: whole ? null : Paper, strokeWidth: whole ? 0 : 2);
                angle += sweep;
            }

            canvas.Text(cx, cy + 2, HoursLabel(total), 20, Ink, anchor: "middle", weight: "700");
            canvas.Text(cx, cy + 22, "in total", 11, Muted, anchor: "middle");

            for (var i = 0; i < categories.Count; i++)
            {
                var x = 24 + (i % 2) * 200;
                var y = 350 + (i / 2) * 20;
                canvas.Rect(x, y - 9, 10, 10, fills[i], radius: 2);
                var share = total > 0 ? categories[i].Hours / total : 0;
                canvas.Text(x + 16, y, $"{Shorten(categories[i].Name, 18)} ({Math.Round(share * 100):0}%)", 11, Ink);
            }
        });
    }

    private static ChartDrawing Trend(ChartTrend trend)
    {
        const double width = 600, height = 340, left = 56, right = 24, top = 40, bottom = 40;
        var count = trend.Labels.Length;
        var all = trend.Total.Concat(trend.Devices.Values.SelectMany(v => v)).DefaultIfEmpty(0);
        var (max, step) = Scale(all.Max() * 1.15);
        var plotHeight = height - top - bottom;
        var plotWidth = width - left - right;
        double X(int i) => left + (count <= 1 ? plotWidth / 2 : plotWidth * i / (count - 1));
        double Y(double v) => top + plotHeight - v / max * plotHeight;

        return new ChartDrawing(width, height, canvas =>
        {
            YAxis(canvas, left, width - right, top, plotHeight, max, step, "hours per week");
            for (var i = 0; i < count; i += LabelEvery(count, 10))
                canvas.Text(X(i), height - bottom + 18, trend.Labels[i], 10.5, Muted, anchor: "middle");

            var legend = new List<(string, string)>();
            var series = 1;
            foreach (var (name, values) in trend.Devices)
            {
                var color = Series[series++ % Series.Length];
                canvas.Polyline(values.Select((v, i) => (X(i), Y(v))), color, 1.4, opacity: 0.85);
                legend.Add((name, color));
            }

            canvas.Polyline(trend.Total.Select((v, i) => (X(i), Y(v))), Accent, 3);
            for (var i = 0; i < trend.Total.Length; i++)
                canvas.Circle(X(i), Y(trend.Total[i]), i == trend.Total.Length - 1 ? 7 : 3.5, Accent, Paper,
                    i == trend.Total.Length - 1 ? 2.5 : 0);
            legend.Insert(0, ("all devices", Accent));
            Legend(canvas, left, 18, legend);
        });
    }

    private static ChartDrawing Heatmap(string[] days, double[][] minutes)
    {
        const double width = 800, left = 104, right = 70, top = 10, cellHeight = 30;
        var height = top + cellHeight * days.Length + 36;
        var cellWidth = (width - left - right) / 24;

        return new ChartDrawing(width, height, canvas =>
        {
            for (var d = 0; d < days.Length; d++)
            {
                canvas.Text(left - 10, top + d * cellHeight + cellHeight / 2 + 4, days[d], 11, Ink, anchor: "end");
                for (var h = 0; h < 24; h++)
                {
                    var value = d < minutes.Length && h < minutes[d].Length ? minutes[d][h] : 0;
                    canvas.Rect(left + h * cellWidth + 1, top + d * cellHeight + 1, cellWidth - 2, cellHeight - 2,
                        HeatColor(value / 60), radius: 3);
                }
            }

            for (var h = 0; h < 24; h += 3)
                canvas.Text(left + h * cellWidth + cellWidth / 2, height - 12, $"{h:00}:00", 10.5, Muted, anchor: "middle");

            // Colour scale
            var barX = width - right + 22;
            var barHeight = cellHeight * days.Length;
            const int steps = 24;
            for (var i = 0; i < steps; i++)
                canvas.Rect(barX, top + barHeight - (i + 1) * barHeight / steps, 12, barHeight / steps + 0.5,
                    HeatColor((i + 0.5) / steps));
            canvas.Text(barX + 16, top + barHeight, "0", 10, Muted);
            canvas.Text(barX + 16, top + barHeight / 2 + 4, "30", 10, Muted);
            canvas.Text(barX + 16, top + 8, "60", 10, Muted);
            canvas.Text(barX + 6, height - 12, "min", 10, Muted, anchor: "middle");
        });
    }

    private static ChartDrawing Device(string[] days, double[] daily, List<ChartApp> apps)
    {
        const double width = 800, height = 260, top = 16, bottom = 34;

        // Left: hours per day
        const double left = 52, plotWidth = 330;
        var plotHeight = height - top - bottom;
        var (max, step) = Scale(daily.DefaultIfEmpty(0).Max() * 1.15);
        double Y(double v) => top + plotHeight - v / max * plotHeight;
        return new ChartDrawing(width, height, canvas =>
        {
            YAxis(canvas, left, left + plotWidth, top, plotHeight, max, step, "hours");
            var column = plotWidth / Math.Max(days.Length, 1);
            for (var i = 0; i < days.Length; i++)
            {
                var bar = column * 0.62;
                var x = left + column * i + (column - bar) / 2;
                if (i < daily.Length && daily[i] > 0)
                    canvas.Rect(x, Y(daily[i]), bar, Y(0) - Y(daily[i]), Accent, radius: 2);
                if (i % LabelEvery(days.Length) == 0)
                    canvas.Text(x + bar / 2, height - bottom + 16, days[i], 10.5, Muted, anchor: "middle");
            }

            // Right: top apps
            if (apps.Count > 0)
            {
                const double nameRight = 560, barRight = 730;
                var appMax = apps.Max(a => a.Hours);
                var row = Math.Min(36, (height - top - bottom) / apps.Count);
                for (var i = 0; i < apps.Count; i++)
                {
                    var y = top + row * i + row / 2;
                    canvas.Text(nameRight - 8, y + 4, Shorten(apps[i].Name, 22), 11, Ink, anchor: "end");
                    var length = appMax > 0 ? apps[i].Hours / appMax * (barRight - nameRight) : 0;
                    canvas.Rect(nameRight, y - 7, Math.Max(length, 1), 14, "#C9A27E", radius: 2);
                    canvas.Text(nameRight + length + 6, y + 4, HoursLabel(apps[i].Hours), 10, Ink);
                }
            }
        });
    }

    /// <summary>Ranked bars (apps, websites): this period against the one before.</summary>
    public static ChartDrawing Ranking(List<ChartApp> items) => Apps(items);

    /// <summary>Minutes per row (days or weekdays) and hour of the day.</summary>
    public static ChartDrawing HeatmapChart(string[] rows, double[][] minutes) => Heatmap(rows, minutes);

    // Building blocks

    /// <summary>Label every n-th column so that at most about <paramref name="labels"/> labels are shown.</summary>
    private static int LabelEvery(int columns, int labels = 14) => Math.Max(1, (int)Math.Ceiling(columns / (double)labels));

    private static void YAxis(IChartCanvas canvas, double left, double right, double top, double height, double max,
        double step, string label)
    {
        for (var tick = 0.0; tick <= max + 1e-9; tick += step)
        {
            var y = top + height - tick / max * height;
            canvas.Line(left, y, right, y, Grid, tick == 0 ? 1 : 0.6);
            canvas.Text(left - 8, y + 4, Number(tick), 10, Muted, anchor: "end");
        }

        canvas.Text(14, top + height / 2, label, 10.5, Muted, anchor: "middle", rotate: -90);
    }

    private static void Legend(IChartCanvas canvas, double x, double y, IEnumerable<(string Name, string Color)> items)
    {
        foreach (var (name, color) in items)
        {
            canvas.Rect(x, y - 9, 10, 10, color, radius: 2);
            var text = Shorten(name, 24);
            canvas.Text(x + 15, y, text, 11, Ink);
            x += 15 + text.Length * 6.6 + 18;
        }
    }

    /// <summary>A rounded upper bound and a tick step giving about four to six grid lines.</summary>
    internal static (double Max, double Step) Scale(double value)
    {
        if (value <= 0)
            return (1, 0.25);
        foreach (var step in new[] { 0.1, 0.25, 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 500 })
            if (value / step <= 6)
                return (Math.Ceiling(value / step) * step, step);
        var big = Math.Pow(10, Math.Ceiling(Math.Log10(value / 6)));
        return (Math.Ceiling(value / big) * big, big);
    }

    /// <summary>Pearl → amber → sardonyx → deep brown, for 0 to 1 (and above).</summary>
    internal static string HeatColor(double t)
    {
        (double R, double G, double B)[] stops =
            [(0xEE, 0xEB, 0xE5), (0xD9, 0xB3, 0x8C), (0xB4, 0x53, 0x2A), (0x3A, 0x1E, 0x0F)];
        t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        var i = Math.Min((int)t, stops.Length - 2);
        var f = t - i;
        var (r, g, b) = (stops[i].R + (stops[i + 1].R - stops[i].R) * f,
            stops[i].G + (stops[i + 1].G - stops[i].G) * f,
            stops[i].B + (stops[i + 1].B - stops[i].B) * f);
        return $"#{(int)r:X2}{(int)g:X2}{(int)b:X2}";
    }

    /// <summary>A ring segment as a polygon (arcs in steps of at most 3°, so it looks round in SVG and PDF).</summary>
    private static List<(double X, double Y)> DonutSlice(double cx, double cy, double outer, double inner, double from,
        double to)
    {
        var steps = Math.Max(2, (int)Math.Ceiling((to - from) / (Math.PI / 60)));
        var points = new List<(double, double)>(2 * steps + 2);
        for (var i = 0; i <= steps; i++)
        {
            var a = from + (to - from) * i / steps;
            points.Add((cx + outer * Math.Cos(a), cy + outer * Math.Sin(a)));
        }

        for (var i = steps; i >= 0; i--)
        {
            var a = from + (to - from) * i / steps;
            points.Add((cx + inner * Math.Cos(a), cy + inner * Math.Sin(a)));
        }

        return points;
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private static string Number(double value) =>
        value % 1 == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    internal static string Escape(string text) => WebUtility.HtmlEncode(text);

    internal sealed class SvgCanvas : IChartCanvas
    {
        private readonly StringBuilder m_Svg = new();

        public SvgCanvas(double width, double height)
        {
            m_Svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(width)} {N(height)}\" " +
                         $"width=\"100%\" role=\"img\" style=\"display:block\">");
            m_Svg.Append($"<rect width=\"{N(width)}\" height=\"{N(height)}\" fill=\"{Paper}\"/>");
        }

        public void Rect(double x, double y, double width, double height, string fill, double radius = 0)
        {
            if (width <= 0 || height <= 0)
                return;
            m_Svg.Append($"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\" " +
                         $"rx=\"{N(Math.Min(radius, Math.Min(width, height) / 2))}\" fill=\"{fill}\"/>");
        }

        public void Line(double x1, double y1, double x2, double y2, string stroke, double width) =>
            m_Svg.Append($"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\" stroke=\"{stroke}\" " +
                         $"stroke-width=\"{N(width)}\"/>");

        public void Text(double x, double y, string text, double size, string fill, string anchor = "start",
            string weight = "400", double rotate = 0) =>
            m_Svg.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" font-size=\"{N(size)}\" font-weight=\"{weight}\" " +
                         $"fill=\"{fill}\" text-anchor=\"{anchor}\" {Font}" +
                         (rotate != 0 ? $" transform=\"rotate({N(rotate)} {N(x)} {N(y)})\"" : "") +
                         $">{Escape(text)}</text>");

        public void Polygon(IReadOnlyList<(double X, double Y)> points, string fill, string? stroke = null,
            double strokeWidth = 0) =>
            m_Svg.Append($"<polygon points=\"{Points(points)}\" fill=\"{fill}\"" +
                         (stroke is null ? "" : $" stroke=\"{stroke}\" stroke-width=\"{N(strokeWidth)}\" stroke-linejoin=\"round\"") + "/>");

        public void Polyline(IEnumerable<(double X, double Y)> points, string stroke, double width, double opacity = 1) =>
            m_Svg.Append($"<polyline points=\"{Points(points)}\" " +
                         $"fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{N(width)}\" stroke-linejoin=\"round\" " +
                         $"stroke-linecap=\"round\" opacity=\"{N(opacity)}\"/>");

        public void Circle(double x, double y, double r, string fill, string stroke, double strokeWidth) =>
            m_Svg.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{N(r)}\" fill=\"{fill}\"" +
                         (strokeWidth > 0 ? $" stroke=\"{stroke}\" stroke-width=\"{N(strokeWidth)}\"" : "") + "/>");

        private static string Points(IEnumerable<(double X, double Y)> points) =>
            string.Join(' ', points.Select(p => $"{N(p.X)},{N(p.Y)}"));

        public override string ToString() => m_Svg + "</svg>";
    }
}

/// <summary>What a chart draws on: SVG (<see cref="ReportCharts.SvgCanvas"/>) or a PDF page
/// (<see cref="Documents.PdfChartCanvas"/>). Coordinates are in the chart's own units, y down.</summary>
public interface IChartCanvas
{
    void Rect(double x, double y, double width, double height, string fill, double radius = 0);
    void Line(double x1, double y1, double x2, double y2, string stroke, double width);

    /// <param name="anchor"><c>start</c>, <c>middle</c> or <c>end</c>.</param>
    /// <param name="weight"><c>400</c> or <c>700</c>.</param>
    /// <param name="rotate">Degrees clockwise around (x, y), as in SVG.</param>
    void Text(double x, double y, string text, double size, string fill, string anchor = "start", string weight = "400",
        double rotate = 0);

    void Polygon(IReadOnlyList<(double X, double Y)> points, string fill, string? stroke = null, double strokeWidth = 0);
    void Polyline(IEnumerable<(double X, double Y)> points, string stroke, double width, double opacity = 1);
    void Circle(double x, double y, double r, string fill, string stroke, double strokeWidth);
}

/// <summary>A chart of a given size that can draw itself on any <see cref="IChartCanvas"/>.</summary>
public sealed record ChartDrawing(double Width, double Height, Action<IChartCanvas> Draw)
{
    public string ToSvg()
    {
        var canvas = new ReportCharts.SvgCanvas(Width, Height);
        Draw(canvas);
        return canvas.ToString();
    }
}
