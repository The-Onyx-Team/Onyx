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

[TestSubject(typeof(IconDownloader))]
public class IconDownloaderTest
{
    private static readonly byte[] s_Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Answers from a fixed set of pages; everything else is 404.</summary>
    private sealed class FakeSite(Dictionary<string, (string Type, byte[] Body)> pages) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested.Add(request.RequestUri!.ToString());
            request.Headers.UserAgent.ToString().ShouldContain("Onyx");
            if (!pages.TryGetValue(request.RequestUri!.ToString(), out var page))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { RequestMessage = request });
            var content = new ByteArrayContent(page.Body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(page.Type);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    [Theory]
    [InlineData("whatsapp.com", "https://whatsapp.com/")]
    [InlineData(" https://example.com/icon.png ", "https://example.com/icon.png")]
    [InlineData("http://example.com", "http://example.com/")]
    public void Normalize_AddsHttps(string input, string expected) =>
        IconDownloader.Normalize(input).ToString().ShouldBe(expected);

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x.png")]
    [InlineData("")]
    public void Normalize_RejectsOtherSchemes(string input) =>
        Should.Throw<InvalidOperationException>(() => IconDownloader.Normalize(input));

    [Fact]
    public void ImageType_SniffsTheContent()
    {
        IconDownloader.ImageType(s_Png, "application/octet-stream").ShouldBe("image/png");
        IconDownloader.ImageType([0, 0, 1, 0, 1, 0], null).ShouldBe("image/x-icon");
        IconDownloader.ImageType("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "text/plain").ShouldBe("image/svg+xml");
        IconDownloader.ImageType("<html></html>"u8.ToArray(), "text/html").ShouldBeNull();
    }

    [Fact]
    public void IconLinks_PreferLargeIcons()
    {
        const string html = """
            <html><head>
            <link rel="icon" href="/favicon-16.png" sizes="16x16">
            <link rel='shortcut icon' href='favicon.ico'>
            <link rel="mask-icon" href="/mask.svg">
            <link href="/apple.png" rel="apple-touch-icon" sizes="180x180">
            <link rel="icon" type="image/png" href="https://cdn.example.com/icon-192.png" sizes="192x192">
            </head></html>
            """;
        IconDownloader.IconLinks(html, new Uri("https://example.com/app/")).Select(u => u.ToString()).ShouldBe([
            "https://example.com/apple.png",
            "https://cdn.example.com/icon-192.png",
            "https://example.com/favicon-16.png",
            "https://example.com/app/favicon.ico"
        ]);
    }

    [Fact]
    public async Task Website_UsesItsIcon()
    {
        var site = new FakeSite(new()
        {
            ["https://example.com/"] = ("text/html", "<link rel=\"icon\" href=\"/missing.png\"><link rel=\"apple-touch-icon\" href=\"/touch.png\">"u8.ToArray()),
            ["https://example.com/touch.png"] = ("image/png", s_Png)
        });

        var (data, type) = await IconDownloader.DownloadAsync(new HttpClient(site), new Uri("https://example.com/"));

        data.ShouldBe(s_Png);
        type.ShouldBe("image/png");
    }

    [Fact]
    public async Task Website_WithoutIconLinks_FallsBackToFavicon()
    {
        var site = new FakeSite(new()
        {
            ["https://example.com/"] = ("text/html", "<html>no icons</html>"u8.ToArray()),
            ["https://example.com/favicon.ico"] = ("image/x-icon", [0, 0, 1, 0, 1, 0, 16, 16])
        });

        var (_, type) = await IconDownloader.DownloadAsync(new HttpClient(site), new Uri("https://example.com/"));

        type.ShouldBe("image/x-icon");
    }

    [Fact]
    public async Task Errors_HaveAMessageForTheUser()
    {
        var site = new FakeSite(new() { ["https://example.com/doc.pdf"] = ("application/pdf", "%PDF-1.4"u8.ToArray()) });

        (await Should.ThrowAsync<InvalidOperationException>(() =>
                IconDownloader.DownloadAsync(new HttpClient(site), new Uri("https://example.com/missing.png"))))
            .Message.ShouldContain("404");
        (await Should.ThrowAsync<InvalidOperationException>(() =>
                IconDownloader.DownloadAsync(new HttpClient(site), new Uri("https://example.com/doc.pdf"))))
            .Message.ShouldContain("not a picture");
    }
}

[TestSubject(typeof(WeeklyReportPdf))]
public class WeeklyReportPdfTest
{
    [Fact]
    public void Report_IsAValidPdf_WithTheChartsDrawnOnFullPages()
    {
        var input = new ChartInput
        {
            Days = ["Mon 16.11.", "Tue 17.11.", "Wed 18.11.", "Thu 19.11.", "Fri 20.11.", "Sat 21.11.", "Sun 22.11."],
            DaysShort = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
            DailyByDevice = new() { ["Phone"] = [1, 2, 0, 1.5, 3, 4, 2] },
            Apps = [new ChartApp("Firefox (Größe)", 3, 1)],
            Categories = [new ChartApp("Social", 5, 0)],
            Trend = new ChartTrend { Labels = ["09.11.", "16.11."], Total = [10, 13.5], Devices = new() },
            Heatmap = Enumerable.Range(0, 7).Select(_ => new double[24]).ToArray(),
            Devices = [new ChartDevice("device-1", [1, 2, 0, 1.5, 3, 4, 2], [new ChartApp("Firefox", 3, 0)])]
        };
        var report = new Onyx.Data.ApiSchema.WeeklyReportDto
        {
            WeekStart = new DateOnly(2026, 11, 16),
            Total = TimeSpan.FromHours(13.5),
            Apps = [new("Firefox", TimeSpan.FromHours(3), TimeSpan.FromHours(1))],
            Devices = [new(1, "Phone", TimeSpan.FromHours(13.5), TimeSpan.Zero, [])],
            Trend = [new("09.11.", TimeSpan.FromHours(10), TimeSpan.Zero), new("16.11.", TimeSpan.FromHours(13.5), TimeSpan.Zero)]
        };

        var pdf = WeeklyReportPdf.Render(report, ReportCharts.Build(input), "Week 47 · 16 – 22 Nov 2026",
            new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc));
        var text = Encoding.Latin1.GetString(pdf);

        text.ShouldStartWith("%PDF-1.4");
        text.ShouldContain("(Weekly report)");
        text.ShouldContain("(Firefox \\(Größe\\))");
        // The paper colour covers the whole page (no browser margins), and charts draw polygons and lines.
        text.ShouldContain("0 0 595.28 841.89 re f");
        text.ShouldContain(" h f");
        text.ShouldContain(" S 0 J 0 j");
        var pages = int.Parse(Regex.Match(text, @"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)").Groups[1].Value);
        text.ShouldContain($"Page {pages} of {pages}");
        var xref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        var entries = Regex.Matches(text[xref..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        for (var i = 0; i < entries.Count; i++)
            text[entries[i]..].ShouldStartWith($"{i + 1} 0 obj");
    }
}

[TestSubject(typeof(CustomReportBuilder))]
public class CustomReportBuilderTest
{
    private static Onyx.Data.ApiSchema.UsageDto Row(string app, string? category, int device, DateTime hourUtc, int minutes,
        string? domain = null) => new()
    {
        AppName = app, Category = category, DeviceId = device, DeviceName = device == 1 ? "Phone" : "Laptop",
        Date = hourUtc, TimeSpan = TimeSpan.FromMinutes(minutes), Domain = domain
    };

    [Fact]
    public void Report_FiltersDevicesAndCategories_AndComparesWithThePeriodBefore()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        List<Onyx.Data.ApiSchema.UsageDto> rows =
        [
            Row("YouTube", "Video", 1, new DateTime(2026, 11, 3, 18, 0, 0, DateTimeKind.Utc), 50),
            Row("YouTube", "Video", 1, new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc), 25), // the period before
            Row("YouTube", "Video", 2, new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc), 40), // other device
            Row("Mail", "Productivity", 1, new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), 30), // other category
            Row("Firefox", "Video", 1, new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc), 20, "youtube.com")
        ];
        var report = new Onyx.Collector.Core.Crypto.CustomReportSettings
        {
            Title = "Videos on the phone", Period = Onyx.Collector.Core.Crypto.ReportPeriod.Custom, From = from, To = to,
            Devices = [1], Apps = new Onyx.Collector.Core.Crypto.AppSelection { Categories = ["Video"] },
            Sections = ["daily", "websites", "categories", "trend", "devices"]
        };

        var layout = CustomReportBuilder.Build(report, rows, from, to, TimeZoneInfo.Utc, null);

        layout.Title.ShouldBe("Videos on the phone");
        layout.Subtitle.ShouldBe("1 Nov 2026 – 30 Nov 2026");
        layout.Stats[0].Value.ShouldBe("1 h 10 min"); // 50 + 20 minutes
        layout.Stats[0].Note.ShouldBe("+180% vs. the 30 days before");
        layout.Charts.Keys.ShouldContain("websites");
        layout.Charts.Keys.ShouldContain("trend");
        layout.Blocks.OfType<SideBySideBlock>().Count().ShouldBe(1); // categories next to the trend
        layout.Blocks.OfType<DeviceBlock>().Single().Heading.ShouldStartWith("Phone");
        Encoding.Latin1.GetString(ReportPdf.Render(layout, DateTime.UtcNow)).ShouldContain("(Videos on the phone)");
    }

    [Theory]
    [InlineData(Onyx.Collector.Core.Crypto.ReportPeriod.Last7Days, "2026-11-18", "2026-11-24")]
    [InlineData(Onyx.Collector.Core.Crypto.ReportPeriod.ThisMonth, "2026-11-01", "2026-11-24")]
    [InlineData(Onyx.Collector.Core.Crypto.ReportPeriod.LastMonth, "2026-10-01", "2026-10-31")]
    [InlineData(Onyx.Collector.Core.Crypto.ReportPeriod.ThisYear, "2026-01-01", "2026-11-24")]
    public void Periods_EndToday(Onyx.Collector.Core.Crypto.ReportPeriod period, string from, string to)
    {
        var (start, end) = new Onyx.Collector.Core.Crypto.CustomReportSettings { Period = period }.Range(new DateOnly(2026, 11, 24));
        start.ShouldBe(DateOnly.Parse(from));
        end.ShouldBe(DateOnly.Parse(to));
    }
}
