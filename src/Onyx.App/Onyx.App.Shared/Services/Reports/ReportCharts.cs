using System.Globalization;
using System.Net;
using System.Text;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>
/// Draws the weekly report's charts as SVG, on the device: the data is end-to-end encrypted, so the server can't
/// draw them. The look follows the printed report: onyx ink on pearl paper with sardonyx accents.
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

    /// <param name="categoryColors">The user's category colours; others get series colours.</param>
    public static Dictionary<string, string> Render(ChartInput input, IReadOnlyDictionary<string, string>? categoryColors = null)
    {
        var charts = new Dictionary<string, string> { ["daily"] = Daily(input.Days, input.DailyByDevice) };
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

    private static string Daily(string[] days, Dictionary<string, double[]> byDevice)
    {
        const double width = 800, height = 340, left = 56, right = 16, top = 34, bottom = 40;
        var svg = new SvgBuilder(width, height);
        var totals = Enumerable.Range(0, days.Length).Select(i => byDevice.Values.Sum(v => v[i])).ToArray();
        var (max, step) = Scale(totals.DefaultIfEmpty(0).Max() * 1.15);
        var plotHeight = height - top - bottom;
        var plotWidth = width - left - right;
        double Y(double value) => top + plotHeight - value / max * plotHeight;

        YAxis(svg, left, width - right, top, plotHeight, max, step, "hours");
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
                    svg.Rect(x, Y(bottomValue + value), bar, Y(bottomValue) - Y(bottomValue + value),
                        Series[series % Series.Length], radius: 2);
                bottomValue += value;
                series++;
            }

            if (totals[i] > 0)
                svg.Text(x + bar / 2, Y(totals[i]) - 6, HoursLabel(totals[i]), 11, Muted, anchor: "middle");
            svg.Text(left + column * i + column / 2, height - bottom + 18, days[i], 11, Muted, anchor: "middle");
        }

        if (byDevice.Count > 1)
            Legend(svg, left, 16, byDevice.Keys.Select((k, i) => (k, Series[i % Series.Length])));
        return svg.ToString();
    }

    private static string Apps(List<ChartApp> apps)
    {
        const double width = 800, right = 70, top = 16, row = 30;
        var nameWidth = Math.Clamp(apps.Max(a => Shorten(a.Name, 28).Length) * 7.2 + 16, 80, 230);
        var left = nameWidth;
        var height = top + row * apps.Count + 44;
        var svg = new SvgBuilder(width, height);
        var (max, step) = Scale(apps.Max(a => Math.Max(a.Hours, a.PreviousHours)) * 1.1);
        var plotWidth = width - left - right;
        double X(double value) => left + value / max * plotWidth;

        for (var tick = 0.0; tick <= max + 1e-9; tick += step)
        {
            svg.Line(X(tick), top, X(tick), top + row * apps.Count, Grid, 0.6);
            svg.Text(X(tick), top + row * apps.Count + 16, Number(tick), 10, Muted, anchor: "middle");
        }

        for (var i = 0; i < apps.Count; i++)
        {
            var y = top + row * i;
            var app = apps[i];
            svg.Text(left - 10, y + row / 2 + 4, Shorten(app.Name, 28), 12, Ink, anchor: "end");
            if (app.PreviousHours > 0)
                svg.Rect(left, y + row / 2 + 1, X(app.PreviousHours) - left, 8, Grid, radius: 2);
            if (app.Hours > 0)
                svg.Rect(left, y + row / 2 - 11, X(app.Hours) - left, 11, Accent, radius: 2);
            svg.Text(X(app.Hours) + 6, y + row / 2 - 2, HoursLabel(app.Hours), 10.5, Ink);
        }

        svg.Text(left + plotWidth / 2, height - 6, "hours", 10.5, Muted, anchor: "middle");
        Legend(svg, width - 250, height - 10, [("this week", Accent), ("week before", Grid)]);
        return svg.ToString();
    }

    private static string Categories(List<ChartApp> categories, IReadOnlyDictionary<string, string> colors)
    {
        const double width = 420, cx = 210, cy = 175, outer = 150, inner = 94;
        var rows = (categories.Count + 1) / 2;
        var height = 350 + rows * 20 + 10;
        var svg = new SvgBuilder(width, height);
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

        var angle = -Math.PI / 2;
        for (var i = 0; i < categories.Count; i++)
        {
            var sweep = total > 0 ? categories[i].Hours / total * 2 * Math.PI : 0;
            if (sweep <= 0)
                continue;
            if (sweep >= 2 * Math.PI - 1e-6)
            {
                svg.Raw($"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N((outer + inner) / 2)}\" fill=\"none\" " +
                        $"stroke=\"{fills[i]}\" stroke-width=\"{N(outer - inner)}\"/>");
            }
            else
            {
                svg.Path(DonutSlice(cx, cy, outer, inner, angle, angle + sweep), fills[i], stroke: Paper, strokeWidth: 2);
            }

            angle += sweep;
        }

        svg.Text(cx, cy + 2, HoursLabel(total), 20, Ink, anchor: "middle", weight: "700");
        svg.Text(cx, cy + 22, "in total", 11, Muted, anchor: "middle");

        for (var i = 0; i < categories.Count; i++)
        {
            var x = 24 + (i % 2) * 200;
            var y = 350 + (i / 2) * 20;
            svg.Rect(x, y - 9, 10, 10, fills[i], radius: 2);
            var share = total > 0 ? categories[i].Hours / total : 0;
            svg.Text(x + 16, y, $"{Shorten(categories[i].Name, 18)} ({Math.Round(share * 100):0}%)", 11, Ink);
        }

        return svg.ToString();
    }

    private static string Trend(ChartTrend trend)
    {
        const double width = 600, height = 340, left = 56, right = 24, top = 40, bottom = 40;
        var svg = new SvgBuilder(width, height);
        var count = trend.Labels.Length;
        var all = trend.Total.Concat(trend.Devices.Values.SelectMany(v => v)).DefaultIfEmpty(0);
        var (max, step) = Scale(all.Max() * 1.15);
        var plotHeight = height - top - bottom;
        var plotWidth = width - left - right;
        double X(int i) => left + (count <= 1 ? plotWidth / 2 : plotWidth * i / (count - 1));
        double Y(double v) => top + plotHeight - v / max * plotHeight;

        YAxis(svg, left, width - right, top, plotHeight, max, step, "hours per week");
        for (var i = 0; i < count; i++)
            svg.Text(X(i), height - bottom + 18, trend.Labels[i], 10.5, Muted, anchor: "middle");

        var legend = new List<(string, string)>();
        var series = 1;
        foreach (var (name, values) in trend.Devices)
        {
            var color = Series[series++ % Series.Length];
            svg.Polyline(values.Select((v, i) => (X(i), Y(v))), color, 1.4, opacity: 0.85);
            legend.Add((name, color));
        }

        svg.Polyline(trend.Total.Select((v, i) => (X(i), Y(v))), Accent, 3);
        for (var i = 0; i < trend.Total.Length; i++)
            svg.Circle(X(i), Y(trend.Total[i]), i == trend.Total.Length - 1 ? 7 : 3.5, Accent, Paper,
                i == trend.Total.Length - 1 ? 2.5 : 0);
        legend.Insert(0, ("all devices", Accent));
        Legend(svg, left, 18, legend);
        return svg.ToString();
    }

    private static string Heatmap(string[] days, double[][] minutes)
    {
        const double width = 800, left = 104, right = 70, top = 10, cellHeight = 30;
        var height = top + cellHeight * days.Length + 36;
        var svg = new SvgBuilder(width, height);
        var cellWidth = (width - left - right) / 24;

        for (var d = 0; d < days.Length; d++)
        {
            svg.Text(left - 10, top + d * cellHeight + cellHeight / 2 + 4, days[d], 11, Ink, anchor: "end");
            for (var h = 0; h < 24; h++)
            {
                var value = d < minutes.Length && h < minutes[d].Length ? minutes[d][h] : 0;
                svg.Rect(left + h * cellWidth + 1, top + d * cellHeight + 1, cellWidth - 2, cellHeight - 2,
                    HeatColor(value / 60), radius: 3);
            }
        }

        for (var h = 0; h < 24; h += 3)
            svg.Text(left + h * cellWidth + cellWidth / 2, height - 12, $"{h:00}:00", 10.5, Muted, anchor: "middle");

        // Colour scale
        var barX = width - right + 22;
        var barHeight = cellHeight * days.Length;
        const int steps = 24;
        for (var i = 0; i < steps; i++)
            svg.Rect(barX, top + barHeight - (i + 1) * barHeight / steps, 12, barHeight / steps + 0.5,
                HeatColor((i + 0.5) / steps));
        svg.Text(barX + 16, top + barHeight, "0", 10, Muted);
        svg.Text(barX + 16, top + barHeight / 2 + 4, "30", 10, Muted);
        svg.Text(barX + 16, top + 8, "60", 10, Muted);
        svg.Text(barX + 6, height - 12, "min", 10, Muted, anchor: "middle");
        return svg.ToString();
    }

    private static string Device(string[] days, double[] daily, List<ChartApp> apps)
    {
        const double width = 800, height = 260, top = 16, bottom = 34;
        var svg = new SvgBuilder(width, height);

        // Left: hours per day
        const double left = 52, plotWidth = 330;
        var plotHeight = height - top - bottom;
        var (max, step) = Scale(daily.DefaultIfEmpty(0).Max() * 1.15);
        double Y(double v) => top + plotHeight - v / max * plotHeight;
        YAxis(svg, left, left + plotWidth, top, plotHeight, max, step, "hours");
        var column = plotWidth / Math.Max(days.Length, 1);
        for (var i = 0; i < days.Length; i++)
        {
            var bar = column * 0.62;
            var x = left + column * i + (column - bar) / 2;
            if (i < daily.Length && daily[i] > 0)
                svg.Rect(x, Y(daily[i]), bar, Y(0) - Y(daily[i]), Accent, radius: 2);
            svg.Text(x + bar / 2, height - bottom + 16, days[i], 10.5, Muted, anchor: "middle");
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
                svg.Text(nameRight - 8, y + 4, Shorten(apps[i].Name, 22), 11, Ink, anchor: "end");
                var length = appMax > 0 ? apps[i].Hours / appMax * (barRight - nameRight) : 0;
                svg.Rect(nameRight, y - 7, Math.Max(length, 1), 14, "#C9A27E", radius: 2);
                svg.Text(nameRight + length + 6, y + 4, HoursLabel(apps[i].Hours), 10, Ink);
            }
        }

        return svg.ToString();
    }

    // Building blocks

    private static void YAxis(SvgBuilder svg, double left, double right, double top, double height, double max,
        double step, string label)
    {
        for (var tick = 0.0; tick <= max + 1e-9; tick += step)
        {
            var y = top + height - tick / max * height;
            svg.Line(left, y, right, y, Grid, tick == 0 ? 1 : 0.6);
            svg.Text(left - 8, y + 4, Number(tick), 10, Muted, anchor: "end");
        }

        svg.Raw($"<text x=\"14\" y=\"{N(top + height / 2)}\" transform=\"rotate(-90 14 {N(top + height / 2)})\" " +
                $"font-size=\"10.5\" fill=\"{Muted}\" text-anchor=\"middle\" {Font}>{Escape(label)}</text>");
    }

    private static void Legend(SvgBuilder svg, double x, double y, IEnumerable<(string Name, string Color)> items)
    {
        foreach (var (name, color) in items)
        {
            svg.Rect(x, y - 9, 10, 10, color, radius: 2);
            var text = Shorten(name, 24);
            svg.Text(x + 15, y, text, 11, Ink);
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

    private static string DonutSlice(double cx, double cy, double outer, double inner, double from, double to)
    {
        var large = to - from > Math.PI ? 1 : 0;
        (double, double) P(double r, double a) => (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        var (x1, y1) = P(outer, from);
        var (x2, y2) = P(outer, to);
        var (x3, y3) = P(inner, to);
        var (x4, y4) = P(inner, from);
        return $"M{N(x1)},{N(y1)} A{N(outer)},{N(outer)} 0 {large} 1 {N(x2)},{N(y2)} L{N(x3)},{N(y3)} " +
               $"A{N(inner)},{N(inner)} 0 {large} 0 {N(x4)},{N(y4)} Z";
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private static string Number(double value) =>
        value % 1 == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    internal static string Escape(string text) => WebUtility.HtmlEncode(text);

    private sealed class SvgBuilder
    {
        private readonly StringBuilder m_Svg = new();

        public SvgBuilder(double width, double height)
        {
            m_Svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(width)} {N(height)}\" " +
                         $"width=\"100%\" role=\"img\" style=\"display:block\">");
            m_Svg.Append($"<rect width=\"{N(width)}\" height=\"{N(height)}\" fill=\"{Paper}\"/>");
        }

        public void Raw(string element) => m_Svg.Append(element);

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
            string weight = "400") =>
            m_Svg.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" font-size=\"{N(size)}\" font-weight=\"{weight}\" " +
                         $"fill=\"{fill}\" text-anchor=\"{anchor}\" {Font}>{Escape(text)}</text>");

        public void Path(string d, string fill, string? stroke = null, double strokeWidth = 0) =>
            m_Svg.Append($"<path d=\"{d}\" fill=\"{fill}\"" +
                         (stroke is null ? "" : $" stroke=\"{stroke}\" stroke-width=\"{N(strokeWidth)}\"") + "/>");

        public void Polyline(IEnumerable<(double X, double Y)> points, string stroke, double width, double opacity = 1) =>
            m_Svg.Append($"<polyline points=\"{string.Join(' ', points.Select(p => $"{N(p.X)},{N(p.Y)}"))}\" " +
                         $"fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{N(width)}\" stroke-linejoin=\"round\" " +
                         $"stroke-linecap=\"round\" opacity=\"{N(opacity)}\"/>");

        public void Circle(double x, double y, double r, string fill, string stroke, double strokeWidth) =>
            m_Svg.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{N(r)}\" fill=\"{fill}\"" +
                         (strokeWidth > 0 ? $" stroke=\"{stroke}\" stroke-width=\"{N(strokeWidth)}\"" : "") + "/>");

        public override string ToString() => m_Svg + "</svg>";
    }
}
