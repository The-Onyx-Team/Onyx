using JetBrains.Annotations;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Collector.Core.Reminders;
using Shouldly;
using Xunit;

namespace UnitTests.Collector;

[TestSubject(typeof(ReminderEngine))]
public class ReminderEngineTest
{
    private static readonly TimeZoneInfo s_Zone = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset s_Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    private static readonly ForegroundApp s_YouTube = new("android:com.google.android.youtube", "YouTube", "Video");
    private static readonly ForegroundApp s_Mail = new("android:com.mail", "Mail", "Productivity");
    private static readonly ForegroundApp s_Code = new("linux:code", "Code", "Development");

    private static ActivityInterval At(ForegroundApp app, int fromMinutesAgo, int toMinutesAgo) =>
        new(app, s_Now.AddMinutes(-fromMinutesAgo), s_Now.AddMinutes(-toMinutesAgo));

    private static VaultSettings Settings(params ReminderSettings[] reminders) =>
        new() { Reminders = [..reminders], Focus = new FocusSettings { Distracting = new AppSelection() } };

    [Fact]
    public void Continuous_FiresAfterTheLimit_AndAgainOnlyAfterAsLongAgain()
    {
        var settings = Settings(new ReminderSettings { Kind = ReminderKind.Continuous, Minutes = 60 });
        var state = new ReminderState();
        // 70 minutes, with a 3 minute pause (shorter than a break)
        List<ActivityInterval> activity = [At(s_Mail, 70, 40), At(s_YouTube, 37, 0)];

        var first = ReminderEngine.Evaluate(settings, 1, activity, s_Now, s_Zone, state);
        first.Count.ShouldBe(1);
        first[0].Message.ShouldContain("1 h 10 min");

        ReminderEngine.Evaluate(settings, 1, activity, s_Now.AddMinutes(30), s_Zone, state).ShouldBeEmpty();
        List<ActivityInterval> later = [..activity, new(s_YouTube, s_Now, s_Now.AddMinutes(61))];
        ReminderEngine.Evaluate(settings, 1, later, s_Now.AddMinutes(61), s_Zone, state).Count.ShouldBe(1);
    }

    [Fact]
    public void Continuous_ABreakStartsOver()
    {
        var settings = Settings(new ReminderSettings { Kind = ReminderKind.Continuous, Minutes = 60 });
        // 40 minutes, a 10 minute break, then 30 minutes
        List<ActivityInterval> activity = [At(s_Mail, 80, 40), At(s_YouTube, 30, 0)];

        ReminderEngine.Evaluate(settings, 1, activity, s_Now, s_Zone, new ReminderState()).ShouldBeEmpty();
    }

    [Fact]
    public void Continuous_OnlyCountsTheChosenAppsAndCategories()
    {
        var settings = Settings(new ReminderSettings
        {
            Kind = ReminderKind.Continuous, Minutes = 30, Apps = new AppSelection { Categories = ["Video"] }
        });
        List<ActivityInterval> mail = [At(s_Mail, 90, 0)];
        List<ActivityInterval> video = [At(s_YouTube, 45, 0)];

        ReminderEngine.Evaluate(settings, 1, mail, s_Now, s_Zone, new ReminderState()).ShouldBeEmpty();
        ReminderEngine.Evaluate(settings, 1, video, s_Now, s_Zone, new ReminderState()).Single().Message
            .ShouldContain("Video apps");
    }

    [Fact]
    public void UserCategories_WinOverReportedOnes()
    {
        var settings = Settings(new ReminderSettings
        {
            Kind = ReminderKind.Continuous, Minutes = 30, Apps = new AppSelection { Categories = ["Social"] }
        });
        settings.Apps[s_Mail.Identifier] = new AppSettings { Category = "Social" };

        ReminderEngine.Evaluate(settings, 1, [At(s_Mail, 45, 0)], s_Now, s_Zone, new ReminderState()).Count.ShouldBe(1);
    }

    [Fact]
    public void Daily_FiresOnceADay_AndOnlyOnChosenDevices()
    {
        var settings = Settings(new ReminderSettings { Kind = ReminderKind.Daily, Minutes = 120, Devices = [7] });
        var state = new ReminderState();
        // 2.5 hours today, plus some yesterday that doesn't count
        List<ActivityInterval> activity = [new(s_Code, s_Now.AddHours(-20), s_Now.AddHours(-16)), At(s_Code, 300, 210), At(s_YouTube, 60, 0)];

        ReminderEngine.Evaluate(settings, 3, activity, s_Now, s_Zone, state).ShouldBeEmpty(); // another device
        ReminderEngine.Evaluate(settings, 7, activity, s_Now, s_Zone, state).Single().Title.ShouldBe("Daily limit reached");
        ReminderEngine.Evaluate(settings, 7, activity, s_Now.AddMinutes(30), s_Zone, state).ShouldBeEmpty();
    }

    [Fact]
    public void DisabledReminders_StayQuiet()
    {
        var settings = Settings(new ReminderSettings { Kind = ReminderKind.Continuous, Minutes = 10, Enabled = false });
        ReminderEngine.Evaluate(settings, 1, [At(s_YouTube, 60, 0)], s_Now, s_Zone, new ReminderState()).ShouldBeEmpty();
    }

    [Fact]
    public void Focus_NudgesAboutDistractingApps_WhileOn()
    {
        var settings = new VaultSettings
        {
            Focus = new FocusSettings { Until = s_Now.AddMinutes(30), Distracting = new AppSelection { Categories = ["Video"] } }
        };
        var state = new ReminderState();

        ReminderEngine.Evaluate(settings, 1, [At(s_Code, 10, 0)], s_Now, s_Zone, state).ShouldBeEmpty();
        var nudge = ReminderEngine.Evaluate(settings, 1, [At(s_YouTube, 2, 0)], s_Now, s_Zone, state).Single();
        nudge.Message.ShouldContain("YouTube");
        nudge.Message.ShouldContain("15:30");
        // Not again within a few minutes, and not once focus mode is over.
        ReminderEngine.Evaluate(settings, 1, [At(s_YouTube, 5, 0)], s_Now.AddMinutes(3), s_Zone, state).ShouldBeEmpty();
        ReminderEngine.Evaluate(settings, 1, [new(s_YouTube, s_Now, s_Now.AddMinutes(40))], s_Now.AddMinutes(40), s_Zone, state)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Focus_OnlyOnChosenDevices_AndNotForAppsInTheBackground()
    {
        var settings = new VaultSettings
        {
            Focus = new FocusSettings { Until = s_Now.AddHours(1), Devices = [2], Distracting = new AppSelection { Apps = [s_YouTube.Identifier] } }
        };

        ReminderEngine.Evaluate(settings, 1, [At(s_YouTube, 2, 0)], s_Now, s_Zone, new ReminderState()).ShouldBeEmpty();
        ReminderEngine.Evaluate(settings, 2, [At(s_YouTube, 20, 10)], s_Now, s_Zone, new ReminderState()).ShouldBeEmpty();
        ReminderEngine.Evaluate(settings, 2, [At(s_YouTube, 2, 0)], s_Now, s_Zone, new ReminderState()).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("2026-11-24T10:00:00", "2026-11-24T12:00:00")] // Tuesday in 9-12
    [InlineData("2026-11-24T12:00:00", null)]
    [InlineData("2026-11-28T10:00:00", null)] // Saturday
    public void Schedules_CoverTheirDaysAndTimes(string local, string? end)
    {
        var schedule = new FocusSchedule(); // weekdays 9:00 to 12:00
        schedule.EndOf(DateTimeOffset.Parse(local + "+00:00"))?.ToString("s").ShouldBe(end);
    }

    [Fact]
    public void Schedules_CanCrossMidnight()
    {
        var schedule = new FocusSchedule { Days = [DayOfWeek.Friday], Start = new TimeOnly(22, 0), End = new TimeOnly(6, 0) };

        schedule.EndOf(new DateTimeOffset(2026, 11, 27, 23, 0, 0, TimeSpan.Zero)).ShouldBe(new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero));
        schedule.EndOf(new DateTimeOffset(2026, 11, 28, 5, 0, 0, TimeSpan.Zero)).ShouldBe(new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero));
        schedule.EndOf(new DateTimeOffset(2026, 11, 28, 22, 30, 0, TimeSpan.Zero)).ShouldBeNull();
    }

    [Fact]
    public void ActivityLog_MergesSamplesOfTheSameApp()
    {
        var log = new ActivityLog();
        log.Add(s_Code, s_Now, s_Now.AddSeconds(2));
        log.Add(s_Code, s_Now.AddSeconds(2), s_Now.AddSeconds(4));
        log.Add(s_YouTube, s_Now.AddSeconds(4), s_Now.AddSeconds(6));

        var intervals = log.Snapshot();
        intervals.Count.ShouldBe(2);
        intervals[0].Duration.ShouldBe(TimeSpan.FromSeconds(4));
    }
}
