using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JetBrains.Annotations;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Documents;
using Onyx.App.Shared.Services.Reports;
using Onyx.App.Shared.Services.Usage;
using Shouldly;
using Xunit;

namespace UnitTests.Client;

[TestSubject(typeof(ReportCharts))]
public class ReportChartsTest
{
    private static ChartInput Input() => new()
    {
        Days = ["Mon 16.11.", "Tue 17.11.", "Wed 18.11.", "Thu 19.11.", "Fri 20.11.", "Sat 21.11.", "Sun 22.11."],
        DaysShort = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
        DailyByDevice = new() { ["Phone"] = [1, 2, 0, 1.5, 3, 4, 2], ["Laptop"] = [3, 2, 1, 0, 2, 0, 0] },
        Apps = [new ChartApp("<script>alert(1)</script> & Co", 5, 4), new ChartApp("Firefox", 3, 0)],
        Categories = [new ChartApp("Social", 5, 0), new ChartApp("Video", 3, 0)],
        Trend = new ChartTrend { Labels = ["02.11.", "09.11.", "16.11."], Total = [10, 12, 21.5], Devices = new() { ["Phone"] = [5, 6, 13.5] } },
        Heatmap = Enumerable.Range(0, 7).Select(d => Enumerable.Range(0, 24).Select(h => (double)(d * h % 61)).ToArray()).ToArray(),
        Devices = [new ChartDevice("device-1", [1, 2, 0, 1.5, 3, 4, 2], [new ChartApp("Firefox", 3, 0)])]
    };

    [Fact]
    public void Charts_AreWellFormedSvg_WithEscapedNames()
    {
        var charts = ReportCharts.Render(Input(), new Dictionary<string, string> { ["Social"] = "#123456" });

        charts.Keys.Order().ShouldBe(["apps", "categories", "daily", "device-1", "heatmap", "trend"]);
        foreach (var svg in charts.Values)
            XDocument.Parse(svg).Root!.Name.LocalName.ShouldBe("svg");

        charts["apps"].ShouldNotContain("<script>");
        charts["apps"].ShouldContain("&lt;script&gt;");
        charts["categories"].ShouldContain("#123456");
        charts["daily"].ShouldContain("Laptop");
    }

    [Theory]
    [InlineData(0, 1, 0.25)]
    [InlineData(5.3, 6, 1)]
    [InlineData(23, 25, 5)]
    public void Scale_GivesRoundAxes(double value, double max, double step) =>
        ReportCharts.Scale(value).ShouldBe((max, step));
}

[TestSubject(typeof(PdfWriter))]
public class PdfWriterTest
{
    [Fact]
    public void Document_IsAValidPdf_WithCorrectCrossReferences()
    {
        var document = new ReportDocument { Title = "Your personal data in Onyx", Subtitle = "Größe · (Klammern) \\ test" };
        document.Section("Account").Values(("Email", "ä@example.com"), ("Emoji", "🙂"));
        document.Section("Apps").Table(["App", "Time"], Enumerable.Range(0, 120).Select(i => new[] { $"App {i}", $"{i} min" }), 1);
        document.Section("Months").Bars([("2026-10", 2, "2 h"), ("2026-11", 4, "4 h")]);

        var pdf = DocumentPdf.Render(document, new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc));
        var text = Encoding.Latin1.GetString(pdf);

        text.ShouldStartWith("%PDF-1.4");
        text.TrimEnd().ShouldEndWith("%%EOF");
        var pages = int.Parse(Regex.Match(text, @"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)").Groups[1].Value);
        pages.ShouldBeGreaterThan(1); // 120 rows don't fit one page
        text.ShouldContain($"Page {pages} of {pages}");
        text.ShouldContain("(Größe · \\(Klammern\\) \\\\ test)");

        // Every xref offset points at the start of its object.
        var xref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        var entries = Regex.Matches(text[xref..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        for (var i = 0; i < entries.Count; i++)
            text[entries[i]..].ShouldStartWith($"{i + 1} 0 obj");
    }

    [Fact]
    public void Wrap_KeepsLinesWithinTheWidth()
    {
        var lines = PdfWriter.Wrap("Onyx records which apps you use and for how long, encrypted on your devices.", 10, 120);
        lines.Count.ShouldBeGreaterThan(1);
        lines.ShouldAllBe(l => PdfWriter.Width(l, 10, false) <= 120);
        PdfWriter.Fit("A very long application name indeed", 10, 60).ShouldEndWith("…");
    }
}

[TestSubject(typeof(AppCatalog))]
public class AppCatalogTest
{
    private static AppEntry Entry(string identifier, string name, string? category = null,
        Onyx.Collector.Core.Crypto.AppSettings? settings = null) =>
        new(identifier, identifier.GetHashCode().ToString(), name, category, null, null, settings);

    [Fact]
    public void SameNameOnSeveralPlatforms_IsOneApp()
    {
        var groups = AppCatalog.Group([
            Entry("android:org.mozilla.firefox", "Firefox", "Social"),
            Entry("linux:firefox", "firefox", "Network"),
            Entry("android:md.obsidian", "Obsidian"),
            Entry("linux:obsidian", "Obsidian ")
        ]);

        groups.Select(g => g.Name).ShouldBe(["Firefox", "Obsidian"]);
        groups[0].Platforms.ShouldBe("Android, Linux");
        groups[0].Category.ShouldBe("Social");
    }

    [Fact]
    public void Renaming_MergesOrSplitsApps()
    {
        var groups = AppCatalog.Group([
            Entry("linux:code", "Code", settings: new() { Name = "VS Code" }),
            Entry("windows:Code.exe", "Visual Studio Code", settings: new() { Name = "VS Code" }),
            Entry("android:a", "Same", settings: new() { Name = "Other" }),
            Entry("linux:a", "Same")
        ]);

        groups.Single(g => g.Name == "VS Code").Members.Count.ShouldBe(2);
        groups.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("Game", "Games")]
    [InlineData("AudioVideo", "Video")]
    [InlineData("Office", "Productivity")]
    [InlineData("Something", "Something")]
    [InlineData(null, null)]
    public void ReportedCategories_MapToTheDefaults(string? reported, string? expected) =>
        AppCatalog.MapReportedCategory(reported).ShouldBe(expected);
}

[TestSubject(typeof(Weeks))]
public class WeeksTest
{
    [Fact]
    public void Numbers_FollowIso8601()
    {
        Weeks.Number(new DateOnly(2026, 11, 23)).ShouldBe(48);
        Weeks.Number(new DateOnly(2027, 1, 1)).ShouldBe(53);
        Weeks.Year(new DateOnly(2027, 1, 1)).ShouldBe(2026);
        Weeks.StartOf(new DateOnly(2026, 11, 29)).ShouldBe(new DateOnly(2026, 11, 23));
        Weeks.LongLabel(new DateOnly(2026, 11, 16)).ShouldBe("Week 47 · 16 – 22 Nov 2026");
        Weeks.LongLabel(new DateOnly(2026, 11, 30)).ShouldBe("Week 49 · 30 Nov – 6 Dec 2026");
    }
}
