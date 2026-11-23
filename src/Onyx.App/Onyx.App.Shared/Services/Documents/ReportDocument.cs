namespace Onyx.App.Shared.Services.Documents;

/// <summary>
/// A simple document (title, sections with paragraphs, key-value lists, tables and bars) that is shown as HTML
/// for the preview and written as PDF for the download, so both always have the same content.
/// </summary>
public sealed class ReportDocument
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public List<DocumentSection> Sections { get; } = [];

    public DocumentSection Section(string heading, string? intro = null)
    {
        var section = new DocumentSection(heading);
        if (intro is not null)
            section.Blocks.Add(new ParagraphBlock(intro));
        Sections.Add(section);
        return section;
    }
}

public sealed class DocumentSection(string heading)
{
    public string Heading { get; } = heading;
    public List<DocumentBlock> Blocks { get; } = [];

    public DocumentSection Paragraph(string text)
    {
        Blocks.Add(new ParagraphBlock(text));
        return this;
    }

    public DocumentSection Values(params (string Key, string? Value)[] values)
    {
        Blocks.Add(new KeyValueBlock(values.Select(v => (v.Key, v.Value ?? "–")).ToList()));
        return this;
    }

    /// <param name="alignRight">Columns aligned right (numbers).</param>
    public DocumentSection Table(string[] headers, IEnumerable<string[]> rows, params int[] alignRight)
    {
        Blocks.Add(new TableBlock(headers, rows.ToList(), alignRight.ToHashSet()));
        return this;
    }

    public DocumentSection Bars(IEnumerable<(string Label, double Value, string Text)> bars)
    {
        Blocks.Add(new BarsBlock(bars.ToList()));
        return this;
    }
}

public abstract record DocumentBlock;

public sealed record ParagraphBlock(string Text) : DocumentBlock;

public sealed record KeyValueBlock(IReadOnlyList<(string Key, string Value)> Values) : DocumentBlock;

public sealed record TableBlock(string[] Headers, IReadOnlyList<string[]> Rows, IReadOnlySet<int> AlignRight)
    : DocumentBlock;

public sealed record BarsBlock(IReadOnlyList<(string Label, double Value, string Text)> Bars) : DocumentBlock;

/// <summary>Lays out a <see cref="ReportDocument"/> on A4 pages.</summary>
public static class DocumentPdf
{
    private const double Margin = 50;
    private const double Bottom = PdfWriter.PageHeight - 56;
    private const double Width = PdfWriter.PageWidth - 2 * Margin;
    private const string Ink = "#131518";
    private const string Muted = "#5B616B";
    private const string Lines = "#DAD6CE";
    private const string Accent = "#B4532A";
    private const string Band = "#EEEBE5";

    public static byte[] Render(ReportDocument document, DateTime created)
    {
        var pdf = new PdfWriter();
        var y = Margin;

        // Title band
        pdf.Rect(0, 0, PdfWriter.PageWidth, 6, Accent);
        y += 18;
        pdf.Text(Margin, y, "ONYX", 9, bold: true, color: Accent);
        y += 24;
        foreach (var line in PdfWriter.Wrap(document.Title, 22, Width, bold: true))
        {
            pdf.Text(Margin, y, line, 22, bold: true);
            y += 26;
        }

        if (document.Subtitle is { } subtitle)
        {
            foreach (var line in PdfWriter.Wrap(subtitle, 10, Width))
            {
                pdf.Text(Margin, y, line, 10, color: Muted);
                y += 14;
            }
        }

        y += 10;

        void Ensure(double height)
        {
            if (y + height <= Bottom)
                return;
            pdf.NewPage();
            pdf.Rect(0, 0, PdfWriter.PageWidth, 3, Accent);
            y = Margin;
        }

        var number = 0;
        foreach (var section in document.Sections)
        {
            number++;
            Ensure(60);
            y += 12;
            pdf.Text(Margin, y, $"{number}  {section.Heading}", 13, bold: true);
            y += 6;
            pdf.Line(Margin, y, Margin + Width, y, Lines, 0.8);
            y += 16;

            foreach (var block in section.Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock p:
                        foreach (var line in PdfWriter.Wrap(p.Text, 10, Width))
                        {
                            Ensure(14);
                            pdf.Text(Margin, y, line, 10);
                            y += 14;
                        }

                        y += 6;
                        break;

                    case KeyValueBlock kv:
                        const double keyWidth = 170;
                        foreach (var (key, value) in kv.Values)
                        {
                            var lines = PdfWriter.Wrap(value, 10, Width - keyWidth);
                            Ensure(14 * lines.Count + 4);
                            pdf.Text(Margin, y, PdfWriter.Fit(key, 10, keyWidth - 10), 10, color: Muted);
                            foreach (var line in lines)
                            {
                                pdf.Text(Margin + keyWidth, y, line, 10);
                                y += 14;
                            }

                            y += 2;
                        }

                        y += 6;
                        break;

                    case TableBlock table:
                        DrawTable(pdf, table, ref y, Ensure);
                        y += 8;
                        break;

                    case BarsBlock bars:
                        var max = bars.Bars.Select(b => b.Value).DefaultIfEmpty(0).Max();
                        foreach (var (label, value, text) in bars.Bars)
                        {
                            Ensure(18);
                            pdf.Text(Margin, y, PdfWriter.Fit(label, 9.5, 150), 9.5);
                            var length = max > 0 ? value / max * (Width - 230) : 0;
                            pdf.Rect(Margin + 160, y - 8, Width - 230, 9, Band);
                            if (length > 0)
                                pdf.Rect(Margin + 160, y - 8, length, 9, Accent);
                            pdf.TextRight(Margin + Width, y, text, 9.5, color: Muted);
                            y += 16;
                        }

                        y += 6;
                        break;
                }
            }
        }

        // Footer with page numbers on every page.
        var total = pdf.PageCount;
        var footer = $"{document.Title} · created {created:yyyy-MM-dd HH:mm} UTC";
        return Stamp(pdf, total, footer, document.Title, created);
    }

    private static byte[] Stamp(PdfWriter pdf, int total, string footer, string title, DateTime created)
    {
        // Footers are drawn last, when the page count is known.
        pdf.AddFooters(total, (page, w) =>
        {
            w.Line(Margin, PdfWriter.PageHeight - 40, Margin + Width, PdfWriter.PageHeight - 40, Lines, 0.5);
            w.Text(Margin, PdfWriter.PageHeight - 26, PdfWriter.Fit(footer, 8, Width - 80), 8, color: Muted);
            w.TextRight(Margin + Width, PdfWriter.PageHeight - 26, $"Page {page} of {total}", 8, color: Muted);
        });
        return pdf.ToBytes(title, created);
    }

    private static void DrawTable(PdfWriter pdf, TableBlock table, ref double y, Action<double> ensure)
    {
        const double size = 9, row = 15;
        var columns = table.Headers.Length;
        // Column widths by content, within the page width.
        var natural = Enumerable.Range(0, columns)
            .Select(c => Math.Max(PdfWriter.Width(table.Headers[c], size, true),
                table.Rows.Select(r => c < r.Length ? PdfWriter.Width(r[c], size) : 0).DefaultIfEmpty(0).Max()) + 12)
            .ToArray();
        var sum = natural.Sum();
        var widths = sum <= Width
            ? natural.Select((w, i) => i == 0 ? w + Width - sum : w).ToArray()
            : natural.Select(w => w / sum * Width).ToArray();

        void Header(ref double yy)
        {
            pdf.Rect(Margin, yy - 11, Width, row, Band);
            var x = Margin;
            for (var c = 0; c < columns; c++)
            {
                var text = PdfWriter.Fit(table.Headers[c], size, widths[c] - 8, bold: true);
                if (table.AlignRight.Contains(c))
                    pdf.TextRight(x + widths[c] - 4, yy, text, size, bold: true);
                else
                    pdf.Text(x + 4, yy, text, size, bold: true);
                x += widths[c];
            }

            yy += row;
        }

        ensure(row * 2);
        Header(ref y);
        foreach (var cells in table.Rows)
        {
            if (y + row > Bottom)
            {
                pdf.NewPage();
                pdf.Rect(0, 0, PdfWriter.PageWidth, 3, Accent);
                y = Margin + 11;
                Header(ref y);
            }

            var x = Margin;
            for (var c = 0; c < columns && c < cells.Length; c++)
            {
                var text = PdfWriter.Fit(cells[c], size, widths[c] - 8);
                if (table.AlignRight.Contains(c))
                    pdf.TextRight(x + widths[c] - 4, y, text, size);
                else
                    pdf.Text(x + 4, y, text, size);
                x += widths[c];
            }

            pdf.Line(Margin, y + 4, Margin + Width, y + 4, Lines, 0.4);
            y += row;
        }
    }
}
