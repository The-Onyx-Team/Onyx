using System.Globalization;
using System.Text;

namespace Onyx.App.Shared.Services.Documents;

/// <summary>
/// A minimal PDF writer: A4 pages with text in Helvetica (the PDF standard font, so nothing is embedded),
/// rectangles and lines. Enough for Onyx's reports, and it runs everywhere .NET does, including WebAssembly.
/// Text is encoded as WinAnsi; characters outside it are replaced.
/// </summary>
public sealed class PdfWriter
{
    public const double PageWidth = 595.28;
    public const double PageHeight = 841.89;

    private readonly List<StringBuilder> m_Pages = [];
    private StringBuilder m_Current = null!;

    public PdfWriter() => NewPage();

    public int PageCount => m_Pages.Count;

    public void NewPage()
    {
        m_Current = new StringBuilder();
        m_Pages.Add(m_Current);
    }

    /// <summary>Draws on every page once all pages exist (footers with "page n of m").</summary>
    public void AddFooters(int total, Action<int, PdfWriter> draw)
    {
        var current = m_Current;
        for (var i = 0; i < m_Pages.Count; i++)
        {
            m_Current = m_Pages[i];
            draw(i + 1, this);
        }

        m_Current = current;
    }

    /// <summary>Draws text with its baseline at <paramref name="y"/> (from the top of the page).</summary>
    public void Text(double x, double y, string text, double size, bool bold = false, string color = "#131518")
    {
        var (r, g, b) = Rgb(color);
        m_Current.Append(Invariant($"BT {r} {g} {b} rg /{(bold ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(PageHeight - y)} Td ("))
            .Append(Escape(text)).Append(") Tj ET\n");
    }

    /// <summary>Draws text ending at <paramref name="right"/>.</summary>
    public void TextRight(double right, double y, string text, double size, bool bold = false, string color = "#131518") =>
        Text(right - Width(text, size, bold), y, text, size, bold, color);

    public void Rect(double x, double y, double width, double height, string color)
    {
        var (r, g, b) = Rgb(color);
        m_Current.Append(Invariant($"{r} {g} {b} rg {N(x)} {N(PageHeight - y - height)} {N(width)} {N(height)} re f\n"));
    }

    public void Line(double x1, double y1, double x2, double y2, string color, double width = 0.5)
    {
        var (r, g, b) = Rgb(color);
        m_Current.Append(Invariant(
            $"{r} {g} {b} RG {N(width)} w {N(x1)} {N(PageHeight - y1)} m {N(x2)} {N(PageHeight - y2)} l S\n"));
    }

    /// <summary>Width of a text in points.</summary>
    public static double Width(string text, double size, bool bold = false)
    {
        var widths = bold ? s_BoldWidths : s_RegularWidths;
        double total = 0;
        foreach (var b in Encode(text))
            total += b is >= 32 and < 127 ? widths[b - 32] : b switch
            {
                0x95 => 350, // bullet
                0x85 => 1000, // ellipsis
                0xB7 => 278, // middle dot
                0xDF => 611, // sharp s
                _ => 556
            };
        return total * size / 1000;
    }

    /// <summary>Splits text into lines no wider than <paramref name="maxWidth"/>.</summary>
    public static List<string> Wrap(string text, double size, double maxWidth, bool bold = false)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Width(candidate, size, bold) <= maxWidth || line.Length == 0)
                {
                    line = candidate;
                    // A single word longer than the line is cut.
                    while (Width(line, size, bold) > maxWidth && line.Length > 1)
                    {
                        var cut = line.Length - 1;
                        while (cut > 1 && Width(line[..cut], size, bold) > maxWidth)
                            cut--;
                        lines.Add(line[..cut]);
                        line = line[cut..];
                    }
                }
                else
                {
                    lines.Add(line);
                    line = word;
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Text that fits <paramref name="maxWidth"/>, with an ellipsis if it was cut.</summary>
    public static string Fit(string text, double size, double maxWidth, bool bold = false)
    {
        if (Width(text, size, bold) <= maxWidth)
            return text;
        var end = text.Length;
        while (end > 0 && Width(text[..end] + "…", size, bold) > maxWidth)
            end--;
        return text[..end] + "…";
    }

    public byte[] ToBytes(string title, DateTime created)
    {
        var output = new MemoryStream();
        var offsets = new List<long>();
        var latin = Encoding.Latin1;

        void Write(string s) => output.Write(latin.GetBytes(s));

        void Object(string body)
        {
            offsets.Add(output.Position);
            Write($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Write("%PDF-1.4\n%âãÏÓ\n");

        // 1 catalog, 2 page tree, 3 and 4 fonts, 5 info, then a page and its content per page.
        const int firstPage = 6;
        var kids = string.Join(" ", Enumerable.Range(0, m_Pages.Count).Select(i => $"{firstPage + i * 2} 0 R"));
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object($"<< /Type /Pages /Kids [{kids}] /Count {m_Pages.Count} >>");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Object($"<< /Title ({Escape(title)}) /Producer (Onyx) /CreationDate (D:{created:yyyyMMddHHmmss}Z) >>");

        for (var i = 0; i < m_Pages.Count; i++)
        {
            Object(Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageWidth)} {N(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {firstPage + i * 2 + 1} 0 R >>"));
            var content = latin.GetBytes(m_Pages[i].ToString());
            offsets.Add(output.Position);
            Write($"{offsets.Count} 0 obj\n<< /Length {content.Length} >>\nstream\n");
            output.Write(content);
            Write("\nendstream\nendobj\n");
        }

        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    /// <summary>Escapes text for a PDF string: WinAnsi bytes written as Latin-1 characters.</summary>
    private static string Escape(string text)
    {
        var result = new StringBuilder();
        foreach (var b in Encode(text))
        {
            switch (b)
            {
                case (byte)'(' or (byte)')' or (byte)'\\':
                    result.Append('\\').Append((char)b);
                    break;
                case < 32:
                    result.Append(' ');
                    break;
                default:
                    result.Append((char)b);
                    break;
            }
        }

        return result.ToString();
    }

    private static readonly Dictionary<char, byte> s_WinAnsi = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['‰'] = 0x89,
        ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92,
        ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['™'] = 0x99,
        ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    internal static IEnumerable<byte> Encode(string text)
    {
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            if (c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ')
                yield return (byte)c;
            else if (s_WinAnsi.TryGetValue(c, out var b))
                yield return b;
            else if (c == '→')
            {
                yield return (byte)'-';
                yield return (byte)'>';
            }
            else if (c is ' ' or ' ' or ' ' or ' ')
                yield return (byte)' ';
            else if (!char.IsLowSurrogate(c))
                yield return (byte)'?';
        }
    }

    private static (string, string, string) Rgb(string hex)
    {
        var value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return (N(((value >> 16) & 255) / 255.0, "0.###"), N(((value >> 8) & 255) / 255.0, "0.###"),
            N((value & 255) / 255.0, "0.###"));
    }

    private static string N(double value, string format = "0.##") => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // Glyph widths of the standard fonts (Adobe AFM), characters 32 to 126, in thousandths of the font size.
    private static readonly int[] s_RegularWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    private static readonly int[] s_BoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    ];
}
