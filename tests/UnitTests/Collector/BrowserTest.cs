using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Extensions.Time.Testing;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Browser;
using Shouldly;
using Xunit;

namespace UnitTests.Collector;

[TestSubject(typeof(BrowserActivity))]
public class BrowserActivityTest
{
    private readonly FakeTimeProvider m_Time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
    private static readonly ForegroundApp s_Chrome = new("linux:google-chrome", "Chrome", ProcessId: 100);
    private static readonly ForegroundApp s_Firefox = new("linux:firefox", "Firefox", ProcessId: 200);

    [Fact]
    public void MatchesReportsToTheBrowserByProcessId()
    {
        var activity = new BrowserActivity(m_Time);
        activity.Update(1, 100, true, "https://www.github.com/the-onyx-team");
        activity.Update(2, 200, true, "en.wikipedia.org");

        activity.GetDomain(s_Chrome).ShouldBe("github.com");
        activity.GetDomain(s_Firefox).ShouldBe("en.wikipedia.org");
        activity.GetDomain(new ForegroundApp("linux:kitty", "Kitty", ProcessId: 300)).ShouldBeNull();
        activity.GetDomain(s_Chrome with { ProcessId = 0 }).ShouldBeNull();
    }

    [Fact]
    public void ProfilesOfOneBrowser_TheFocusedOneWins()
    {
        var activity = new BrowserActivity(m_Time);
        activity.Update(1, 100, true, "github.com");
        m_Time.Advance(TimeSpan.FromSeconds(1));
        // Focus moved to another profile's window: it reports focused, the first profile reports unfocused.
        activity.Update(2, 100, true, "example.org");
        m_Time.Advance(TimeSpan.FromMilliseconds(10));
        activity.Update(1, 100, false, "github.com");

        activity.GetDomain(s_Chrome).ShouldBe("example.org");
    }

    [Fact]
    public void ExcludedDomainsAndSubdomainsAreNotReported()
    {
        var activity = new BrowserActivity(m_Time);
        activity.SetExcludedDomains(["example.com"]);

        activity.Update(1, 100, true, "mail.example.com");
        activity.GetDomain(s_Chrome).ShouldBeNull();

        activity.Update(1, 100, true, "notexample.com");
        activity.GetDomain(s_Chrome).ShouldBe("notexample.com");
    }

    [Fact]
    public void DisconnectedExtensionsAreForgotten()
    {
        var activity = new BrowserActivity(m_Time);
        activity.Update(1, 100, true, "github.com");
        activity.Disconnected(1);

        activity.GetDomain(s_Chrome).ShouldBeNull();
        activity.Connections.ShouldBe(0);
    }
}

[TestSubject(typeof(NativeHostRelay))]
public class NativeHostRelayTest
{
    private static async Task<MemoryStream> Frames(params string[] messages)
    {
        var input = new MemoryStream();
        foreach (var message in messages)
            await NativeHostRelay.WriteMessageAsync(input, message);
        input.Position = 0;
        return input;
    }

    [Fact]
    public async Task ForwardsExtensionMessagesWithTheBrowserProcess()
    {
        var collector = new MemoryStream();
        await using var relay = new NativeHostRelay(4242, _ => Task.FromResult<Stream>(new NonClosingStream(collector)));

        var output = new MemoryStream();
        await relay.RunAsync(await Frames("""{"focused":true,"domain":"github.com"}""", "not json",
            """{"focused":false,"domain":null}"""), output);

        var lines = System.Text.Encoding.UTF8.GetString(collector.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        JsonSerializer.Deserialize<JsonElement>(lines[0]).GetProperty("browserPid").GetInt32().ShouldBe(4242);
        JsonSerializer.Deserialize<JsonElement>(lines[0]).GetProperty("domain").GetString().ShouldBe("github.com");
        JsonSerializer.Deserialize<JsonElement>(lines[1]).GetProperty("focused").GetBoolean().ShouldBeFalse();

        // The extension is told once that the collector is reachable.
        output.Position = 0;
        (await NativeHostRelay.ReadMessageAsync(output)).ShouldBe("""{"collector":true}""");
        (await NativeHostRelay.ReadMessageAsync(output)).ShouldBeNull();
    }

    [Fact]
    public async Task CollectorNotRunning_DropsMessagesAndTellsTheExtension()
    {
        await using var relay = new NativeHostRelay(1, _ => throw new TimeoutException());
        var output = new MemoryStream();

        await relay.RunAsync(await Frames("""{"focused":true,"domain":"github.com"}"""), output);

        output.Position = 0;
        (await NativeHostRelay.ReadMessageAsync(output)).ShouldBe("""{"collector":false}""");
    }

    [Fact]
    public async Task OversizedFrame_IsRejected()
    {
        var input = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F]);
        await Should.ThrowAsync<InvalidDataException>(() => NativeHostRelay.ReadMessageAsync(input));
    }

    /// <summary>Keeps the captured bytes readable after the relay disposes its connection.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}

public class WebsiteCollectionTest
{
    private readonly FakeTimeProvider m_Time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));

    private sealed class Source(ForegroundApp app) : IForegroundAppSource
    {
        public ForegroundApp? GetForegroundApp() => app;
    }

    [Fact]
    public void Tracker_SplitsBrowserTimeByWebsite()
    {
        var browser = new BrowserActivity(m_Time);
        var store = new PendingUsageStore(null);
        var chrome = new ForegroundApp("windows:chrome.exe", "Chrome", ProcessId: 7);
        var tracker = new ForegroundTracker(new Source(chrome), null, store, m_Time,
            new ForegroundTrackerOptions { PollInterval = TimeSpan.FromSeconds(2) }, websiteSource: browser);

        browser.Update(1, 7, true, "github.com");
        for (var i = 0; i < 31; i++) { m_Time.Advance(TimeSpan.FromSeconds(2)); tracker.Sample(); }
        browser.Update(1, 7, true, "news.ycombinator.com");
        for (var i = 0; i < 15; i++) { m_Time.Advance(TimeSpan.FromSeconds(2)); tracker.Sample(); }
        browser.Update(1, 7, true, null); // a new tab page
        for (var i = 0; i < 5; i++) { m_Time.Advance(TimeSpan.FromSeconds(2)); tracker.Sample(); }

        // A tab switch is picked up at the next poll, so each website keeps the 2 s sample after it.
        var entries = store.Snapshot().ToDictionary(e => e.Domain ?? "");
        entries["github.com"].ForegroundTime.ShouldBe(TimeSpan.FromSeconds(62));
        entries["news.ycombinator.com"].ForegroundTime.ShouldBe(TimeSpan.FromSeconds(30));
        entries[""].ForegroundTime.ShouldBe(TimeSpan.FromSeconds(8));
        entries.Values.ShouldAllBe(e => e.AppIdentifier == "windows:chrome.exe");
    }

    [Fact]
    public void Store_KeepsDomainsAcrossSaveAndLoad_AndReadsOldFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onyx-test-{Guid.NewGuid()}.json");
        try
        {
            var app = new ForegroundApp("linux:firefox", "Firefox");
            var start = m_Time.GetUtcNow();
            var store = new PendingUsageStore(path);
            store.Add(app, start, start.AddMinutes(5), "github.com");
            store.Add(app, start, start.AddMinutes(2));
            store.Save();

            var loaded = new PendingUsageStore(path).Snapshot();
            loaded.Count.ShouldBe(2);
            loaded.Single(e => e.Domain == "github.com").ForegroundTime.ShouldBe(TimeSpan.FromMinutes(5));
            loaded.Single(e => e.Domain == null).ForegroundTime.ShouldBe(TimeSpan.FromMinutes(2));

            // A file written before websites existed has no Domain property.
            File.WriteAllText(path, """[{"App":"linux:kitty","AppName":"Kitty","Category":null,"Hour":"2026-11-10T09:00:00Z","Time":"00:03:00"}]""");
            new PendingUsageStore(path).Snapshot().Single().Domain.ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class WholeHourUploadTest
{
    private static readonly DateTime s_Hour = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Acknowledge_KeepsAllWebsitesOfAnHourIfOneGrew()
    {
        var store = new PendingUsageStore(null);
        var app = new ForegroundApp("linux:firefox", "Firefox");
        var start = new DateTimeOffset(s_Hour);
        store.Add(app, start, start.AddMinutes(10), "github.com");
        store.Add(app, start.AddMinutes(10), start.AddMinutes(15), "example.org");

        var snapshot = store.Snapshot();
        store.Add(app, start.AddMinutes(15), start.AddMinutes(16), "github.com"); // grew during the upload
        store.Acknowledge(snapshot, s_Hour.AddHours(1));

        // Both stay, so the next upload sends the complete hour again.
        store.Snapshot().Select(e => e.Domain).ShouldBe(["github.com", "example.org"], ignoreOrder: true);
    }

    [Fact]
    public void ChunkByHour_NeverSplitsAnHour()
    {
        var entries = Enumerable.Range(0, 3)
            .SelectMany(h => Enumerable.Range(0, 2000).Select(i => new Onyx.Data.ApiSchema.UsageEntryDto(
                $"app{i}", "App", null, s_Hour.AddHours(h), TimeSpan.FromMinutes(1))))
            .ToList();

        var chunks = OnyxApiClient.ChunkByHour(entries).ToList();

        chunks.Count.ShouldBe(2);
        chunks.ShouldAllBe(c => c.Length <= OnyxApiClient.MaxEntriesPerUpload);
        chunks.SelectMany(c => c.Select(e => e.BucketStart).Distinct()).Count().ShouldBe(3);
        chunks.Sum(c => c.Length).ShouldBe(6000);
    }
}
