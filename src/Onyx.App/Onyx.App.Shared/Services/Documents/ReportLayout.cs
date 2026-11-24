using Onyx.App.Shared.Services.Reports;

namespace Onyx.App.Shared.Services.Documents;

/// <summary>
/// A usage report as blocks of charts and text: the weekly report and the user's own reports. Shown on the page
/// (<c>ReportLayoutView</c>, charts as SVG) and written as PDF (<see cref="ReportPdf"/>) from the same layout.
/// </summary>
/// <param name="FileTitle">Title of the PDF document and its footer.</param>
public sealed record ReportLayout(
    string Title,
    string Subtitle,
    IReadOnlyList<ReportStat> Stats,
    IReadOnlyList<ReportBlock> Blocks,
    IReadOnlyDictionary<string, ChartDrawing> Charts,
    string FileTitle);

/// <summary>A key figure at the top: "Screen time / 85 h 34 min / +29% vs. the week before".</summary>
public sealed record ReportStat(string Caption, string Value, string Note);

public abstract record ReportBlock;

/// <param name="Key">The chart in <see cref="ReportLayout.Charts"/>; the block is left out if it isn't there.</param>
public sealed record ChartBlock(string Key, string? Title = null, string? Caption = null) : ReportBlock;

/// <summary>Two charts next to each other (one below the other on narrow screens).</summary>
/// <param name="LeftShare">Share of the width the left chart gets.</param>
public sealed record SideBySideBlock(ChartBlock Left, ChartBlock Right, double LeftShare = 0.42) : ReportBlock;

public sealed record TextBlock(string Text) : ReportBlock;

public sealed record HeadingBlock(string Text) : ReportBlock;

/// <summary>A device: its time, the change, and its chart.</summary>
public sealed record DeviceBlock(string Heading, string Note, string ChartKey) : ReportBlock;

/// <summary>
/// Writes a <see cref="ReportLayout"/> as an A4 PDF, drawn on the device (the data is end-to-end encrypted). Pages are
/// filled edge to edge with the report's paper colour and carry only Onyx's own footer, unlike a browser printout
/// with its headers, margins and URLs.
/// </summary>
public static class ReportPdf
{
    private const double Margin = 40;
    private const double Width = PdfWriter.PageWidth - 2 * Margin;
    private const double Bottom = PdfWriter.PageHeight - 50;
    private const double Gap = 16;
    private const string Band = "#EEEBE5";

    public static byte[] Render(ReportLayout layout, DateTime created)
    {
        var pdf = new PdfWriter();
        Background(pdf);
        var y = Margin + 8;

        void Ensure(double height)
        {
            if (y + height <= Bottom)
                return;
            pdf.NewPage();
            Background(pdf);
            y = Margin;
        }

        // Header
        pdf.Text(Margin, y, "ONYX", 9, bold: true, color: ReportCharts.Accent);
        pdf.TextRight(Margin + Width, y, "end-to-end encrypted · built on your device", 8, color: ReportCharts.Muted);
        y += 26;
        foreach (var line in PdfWriter.Wrap(layout.Title, 24, Width, bold: true))
        {
            pdf.Text(Margin, y, line, 24, bold: true);
            y += 26;
        }

        y -= 8;
        pdf.Text(Margin, y, PdfWriter.Fit(layout.Subtitle, 11, Width), 11, color: ReportCharts.Muted);
        y += 22;

        // Key figures
        if (layout.Stats.Count > 0)
        {
            const double gap = 8;
            var count = layout.Stats.Count;
            var tile = (Width - (count - 1) * gap) / count;
            for (var i = 0; i < count; i++)
            {
                var stat = layout.Stats[i];
                var x = Margin + i * (tile + gap);
                pdf.Rect(x, y, tile, 58, Band);
                pdf.Text(x + 10, y + 16, PdfWriter.Fit(stat.Caption, 8, tile - 20), 8, color: ReportCharts.Muted);
                pdf.Text(x + 10, y + 35, PdfWriter.Fit(stat.Value, 14, tile - 20, bold: true), 14, bold: true);
                pdf.Text(x + 10, y + 50, PdfWriter.Fit(stat.Note, 8, tile - 20), 8, color: ReportCharts.Muted);
            }

            y += 58 + 22;
        }

        double HeadHeight(ChartBlock block) => (block.Title is null ? 0 : 18) + (block.Caption is null ? 0 : 13);

        // Draws a chart at (x, top) in the given width; returns its bottom.
        double Chart(ChartBlock block, ChartDrawing chart, double x, double top, double width)
        {
            var yy = top;
            if (block.Title is not null)
            {
                pdf.Text(x, yy + 12, block.Title, 13, bold: true);
                yy += 18;
            }

            if (block.Caption is not null)
            {
                pdf.Text(x, yy + 9, PdfWriter.Fit(block.Caption, 8.5, width), 8.5, color: ReportCharts.Muted);
                yy += 13;
            }

            var scale = width / chart.Width;
            chart.Draw(new PdfChartCanvas(pdf, x, yy, scale));
            return yy + chart.Height * scale;
        }

        foreach (var block in layout.Blocks)
        {
            switch (block)
            {
                case ChartBlock c when layout.Charts.TryGetValue(c.Key, out var chart):
                    Ensure(HeadHeight(c) + chart.Height * Width / chart.Width);
                    y = Chart(c, chart, Margin, y, Width) + Gap;
                    break;

                case SideBySideBlock s:
                {
                    var left = layout.Charts.GetValueOrDefault(s.Left.Key);
                    var right = layout.Charts.GetValueOrDefault(s.Right.Key);
                    if (left is null || right is null)
                    {
                        // Only one of them: full width.
                        var (single, drawing) = left is not null ? (s.Left, left) : (s.Right, right);
                        if (drawing is not null)
                        {
                            Ensure(HeadHeight(single) + drawing.Height * Width / drawing.Width);
                            y = Chart(single, drawing, Margin, y, Width) + Gap;
                        }

                        break;
                    }

                    var leftWidth = Width * s.LeftShare;
                    var rightWidth = Width - leftWidth - Gap;
                    Ensure(Math.Max(HeadHeight(s.Left) + left.Height * leftWidth / left.Width,
                        HeadHeight(s.Right) + right.Height * rightWidth / right.Width));
                    var bottom = Math.Max(Chart(s.Left, left, Margin, y, leftWidth),
                        Chart(s.Right, right, Margin + leftWidth + Gap, y, rightWidth));
                    y = bottom + Gap;
                    break;
                }

                case TextBlock t:
                    foreach (var line in PdfWriter.Wrap(t.Text, 9.5, Width))
                    {
                        Ensure(14);
                        pdf.Text(Margin, y, line, 9.5);
                        y += 14;
                    }

                    y += 10;
                    break;

                case HeadingBlock h:
                    Ensure(60);
                    pdf.Text(Margin, y + 12, h.Text, 13, bold: true);
                    y += 26;
                    break;

                case DeviceBlock d:
                {
                    var chart = layout.Charts.GetValueOrDefault(d.ChartKey);
                    Ensure(18 + (chart is null ? 0 : chart.Height * Width / chart.Width));
                    pdf.Text(Margin, y + 10, d.Heading, 11, bold: true);
                    pdf.Text(Margin + PdfWriter.Width(d.Heading, 11, bold: true) + 6, y + 10, d.Note, 9,
                        color: ReportCharts.Muted);
                    y += 18;
                    if (chart is not null)
                        y = Chart(new ChartBlock(d.ChartKey), chart, Margin, y, Width) + Gap;
                    break;
                }
            }
        }

        var total = pdf.PageCount;
        var footer = $"{layout.FileTitle} · created {created:yyyy-MM-dd HH:mm} UTC";
        pdf.AddFooters(total, (page, w) =>
        {
            w.Line(Margin, PdfWriter.PageHeight - 34, Margin + Width, PdfWriter.PageHeight - 34, ReportCharts.Grid, 0.5);
            w.Text(Margin, PdfWriter.PageHeight - 22, PdfWriter.Fit(footer, 8, Width - 80), 8, color: ReportCharts.Muted);
            w.TextRight(Margin + Width, PdfWriter.PageHeight - 22, $"Page {page} of {total}", 8, color: ReportCharts.Muted);
        });
        return pdf.ToBytes(layout.FileTitle, created);
    }

    private static void Background(PdfWriter pdf)
    {
        pdf.Rect(0, 0, PdfWriter.PageWidth, PdfWriter.PageHeight, ReportCharts.Paper);
        pdf.Rect(0, 0, PdfWriter.PageWidth, 6, ReportCharts.Accent);
    }
}
