using JetBrains.Annotations;
using Microsoft.Extensions.Time.Testing;
using Onyx.Collector.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Collector;

[TestSubject(typeof(UsageBuckets))]
public class UsageBucketsTest
{
    private static readonly ForegroundApp s_App = new("linux:firefox", "Firefox");
    private static readonly DateTimeOffset s_Start = new(2026, 11, 9, 13, 50, 0, TimeSpan.Zero);

    [Fact]
    public void FromIntervals_SplitsAtHourBoundaries()
    {
        var entries = UsageBuckets.FromIntervals([(s_App, s_Start, s_Start.AddMinutes(25))]);

        entries.Count.ShouldBe(2);
        entries[0].BucketStart.ShouldBe(new DateTime(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc));
        entries[0].ForegroundTime.ShouldBe(TimeSpan.FromMinutes(10));
        entries[1].BucketStart.ShouldBe(new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc));
        entries[1].ForegroundTime.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void FromIntervals_UsesUtcForOffsetTimes()
    {
        var local = new DateTimeOffset(2026, 11, 9, 15, 10, 0, TimeSpan.FromHours(1));

        var entry = UsageBuckets.FromIntervals([(s_App, local, local.AddMinutes(5))]).ShouldHaveSingleItem();

        entry.BucketStart.ShouldBe(new DateTime(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void FromIntervals_CountsOpensInTheHourTheyHappened_EvenWithoutTime()
    {
        var device = new ForegroundApp(Onyx.Data.ApiSchema.DeviceActivity.Identifier, "Device unlocks");
        var entries = UsageBuckets.FromIntervals(
            [(s_App, s_Start, s_Start.AddMinutes(25))],
            [(s_App, s_Start), (s_App, s_Start.AddMinutes(15)), (device, s_Start.AddMinutes(20))]);

        entries.Single(e => e.AppIdentifier == "linux:firefox" && e.BucketStart.Hour == 13).Opens.ShouldBe(1);
        entries.Single(e => e.AppIdentifier == "linux:firefox" && e.BucketStart.Hour == 14).Opens.ShouldBe(1);
        var unlocks = entries.Single(e => e.AppIdentifier == device.Identifier);
        unlocks.Opens.ShouldBe(1);
        unlocks.ForegroundTime.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void FromIntervals_CapsOverlappingIntervalsAtOneHour()
    {
        var hour = new DateTimeOffset(2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

        var entry = UsageBuckets.FromIntervals([(s_App, hour, hour.AddMinutes(50)), (s_App, hour.AddMinutes(10), hour.AddMinutes(60))])
            .ShouldHaveSingleItem();

        entry.ForegroundTime.ShouldBe(TimeSpan.FromHours(1));
    }
}

[TestSubject(typeof(PendingUsageStore))]
public class PendingUsageStoreTest
{
    private static readonly ForegroundApp s_App = new("linux:firefox", "Firefox");
    private static readonly DateTimeOffset s_Hour = new(2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Acknowledge_DropsFinishedHours_KeepsCurrentHour()
    {
        var store = new PendingUsageStore(null);
        store.Add(s_App, s_Hour.AddMinutes(-10), s_Hour.AddMinutes(10));

        store.Acknowledge(store.Snapshot(), s_Hour.UtcDateTime);

        var left = store.Snapshot().ShouldHaveSingleItem();
        left.BucketStart.ShouldBe(s_Hour.UtcDateTime);
    }

    [Fact]
    public void AddOpen_CountsWithoutTime_AndAcknowledgeKeepsHoursWithNewOpens()
    {
        var store = new PendingUsageStore(null);
        store.AddOpen(s_App, s_Hour.AddMinutes(-30));
        store.AddOpen(s_App, s_Hour.AddMinutes(-20));
        var snapshot = store.Snapshot();
        snapshot.ShouldHaveSingleItem().Opens.ShouldBe(2);
        snapshot[0].ForegroundTime.ShouldBe(TimeSpan.Zero);

        store.AddOpen(s_App, s_Hour.AddMinutes(-10));
        store.Acknowledge(snapshot, s_Hour.UtcDateTime);

        store.Snapshot().ShouldHaveSingleItem().Opens.ShouldBe(3);
        store.Acknowledge(store.Snapshot(), s_Hour.UtcDateTime);
        store.Count.ShouldBe(0);
    }

    [Fact]
    public void Acknowledge_KeepsHoursThatGrewDuringTheUpload()
    {
        var store = new PendingUsageStore(null);
        store.Add(s_App, s_Hour.AddMinutes(-30), s_Hour.AddMinutes(-20));
        var snapshot = store.Snapshot();
        store.Add(s_App, s_Hour.AddMinutes(-5), s_Hour);

        store.Acknowledge(snapshot, s_Hour.AddHours(1).UtcDateTime);

        store.Snapshot().ShouldHaveSingleItem().ForegroundTime.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Save_And_Load_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onyx-pending-{Guid.NewGuid()}.json");
        try
        {
            var store = new PendingUsageStore(path);
            store.Add(s_App, s_Hour, s_Hour.AddMinutes(7));
            store.AddOpen(s_App, s_Hour);
            store.Save();

            var entry = new PendingUsageStore(path).Snapshot().ShouldHaveSingleItem();
            entry.AppIdentifier.ShouldBe("linux:firefox");
            entry.BucketStart.Kind.ShouldBe(DateTimeKind.Utc);
            entry.ForegroundTime.ShouldBe(TimeSpan.FromMinutes(7));
            entry.Opens.ShouldBe(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onyx-pending-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            new PendingUsageStore(path).Count.ShouldBe(0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[TestSubject(typeof(ForegroundTracker))]
public class ForegroundTrackerTest
{
    private sealed class FakeSource : IForegroundAppSource, IIdleTimeSource
    {
        public ForegroundApp? App { get; set; }
        public TimeSpan Idle { get; set; }
        public bool Throw { get; set; }

        public ForegroundApp? GetForegroundApp() => Throw ? throw new InvalidOperationException("gone") : App;
        public TimeSpan? GetIdleTime() => Idle;
    }

    private readonly FakeTimeProvider m_Time = new(new DateTimeOffset(2026, 11, 9, 13, 0, 0, TimeSpan.Zero));
    private readonly FakeSource m_Source = new();
    private readonly PendingUsageStore m_Store = new(null);
    private readonly ForegroundTracker m_Tracker;

    public ForegroundTrackerTest()
    {
        m_Tracker = new ForegroundTracker(m_Source, m_Source, m_Store, m_Time,
            new ForegroundTrackerOptions { PollInterval = TimeSpan.FromSeconds(2) });
    }

    private void Step(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            m_Time.Advance(TimeSpan.FromSeconds(2));
            m_Tracker.Sample();
        }
    }

    [Fact]
    public void CreditsTimeToTheAppInFront()
    {
        m_Tracker.Sample();
        m_Source.App = new ForegroundApp("linux:code", "Code");
        Step(); // sees Code
        Step(5); // 10 s of Code

        m_Store.Snapshot().ShouldHaveSingleItem().ForegroundTime.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DoesNotCountIdleTime()
    {
        m_Source.App = new ForegroundApp("linux:code", "Code");
        m_Tracker.Sample();
        m_Source.Idle = TimeSpan.FromMinutes(10);
        Step(10);

        m_Store.Snapshot().ShouldHaveSingleItem().ForegroundTime.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void DoesNotCountGapsLikeSuspend()
    {
        m_Source.App = new ForegroundApp("linux:code", "Code");
        m_Tracker.Sample();
        m_Time.Advance(TimeSpan.FromHours(2));
        m_Tracker.Sample();

        m_Store.Snapshot().Sum(e => e.ForegroundTime.Ticks).ShouldBe(0);
    }

    [Fact]
    public void CountsAnOpenWhenAnotherAppComesToTheFront_NotEverySample()
    {
        var code = new ForegroundApp("linux:code", "Code");
        var chat = new ForegroundApp("linux:chat", "Chat");
        m_Tracker.Sample();
        m_Source.App = code;
        Step(5);
        m_Source.App = chat;
        Step(3);
        m_Source.App = code;
        Step(2);

        var entries = m_Store.Snapshot();
        entries.Single(e => e.AppIdentifier == "linux:code").Opens.ShouldBe(2);
        entries.Single(e => e.AppIdentifier == "linux:chat").Opens.ShouldBe(1);
    }

    [Fact]
    public void CountsTheSameAppAgainAfterIdleOrSuspend_ButNotAfterASampleWithoutWindow()
    {
        m_Source.App = new ForegroundApp("linux:code", "Code");
        m_Tracker.Sample(); // open 1
        m_Source.Idle = TimeSpan.FromMinutes(10);
        Step(2);
        m_Source.Idle = TimeSpan.Zero;
        Step(); // open 2: back from idle
        m_Time.Advance(TimeSpan.FromHours(1));
        m_Tracker.Sample(); // open 3: back from suspend
        m_Source.App = null;
        Step();
        m_Source.App = new ForegroundApp("linux:code", "Code");
        Step(); // the same app again, no break

        m_Store.Snapshot().Sum(e => e.Opens).ShouldBe(3);
    }

    [Fact]
    public void CountsWebsiteVisits_SeparateFromBrowserOpens()
    {
        var websites = new FakeWebsites();
        var tracker = new ForegroundTracker(m_Source, m_Source, m_Store, m_Time,
            new ForegroundTrackerOptions { PollInterval = TimeSpan.FromSeconds(2) }, websiteSource: websites);
        m_Source.App = new ForegroundApp("linux:firefox", "Firefox");
        websites.Domain = "github.com";
        tracker.Sample();
        m_Time.Advance(TimeSpan.FromSeconds(2));
        tracker.Sample();
        websites.Domain = "example.org";
        m_Time.Advance(TimeSpan.FromSeconds(2));
        tracker.Sample();
        websites.Domain = null; // the extension hasn't answered yet
        m_Time.Advance(TimeSpan.FromSeconds(2));
        tracker.Sample();
        websites.Domain = "example.org";
        m_Time.Advance(TimeSpan.FromSeconds(2));
        tracker.Sample();

        var entries = m_Store.Snapshot();
        entries.Single(e => e.Domain is null).Opens.ShouldBe(1);
        entries.Single(e => e.Domain == "github.com").Opens.ShouldBe(1);
        entries.Single(e => e.Domain == "example.org").Opens.ShouldBe(1);
    }

    private sealed class FakeWebsites : Onyx.Collector.Core.Browser.IWebsiteSource
    {
        public string? Domain { get; set; }
        public string? GetDomain(ForegroundApp app) => Domain;
    }

    [Fact]
    public void SurvivesSourceExceptions()
    {
        m_Source.Throw = true;
        m_Tracker.Sample();
        Step(3);
        m_Source.Throw = false;
        m_Source.App = new ForegroundApp("linux:code", "Code");
        Step(2);

        m_Store.Snapshot().ShouldHaveSingleItem().ForegroundTime.ShouldBe(TimeSpan.FromSeconds(2));
    }
}
