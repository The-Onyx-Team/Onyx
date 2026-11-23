using JetBrains.Annotations;
using Onyx.App.Shared.Services.Goals;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Client;

[TestSubject(typeof(GoalEvaluator))]
public class GoalEvaluatorTest
{
    private static readonly TimeZoneInfo s_Zone = TimeZoneInfo.Utc;
    private static readonly DateOnly s_Today = new(2026, 11, 23); // a Monday

    private static UsageDto Use(DateOnly day, int minutes, string app = "Instagram", string? category = "Social", int device = 1) => new()
    {
        AppId = 1, AppName = app, Category = category, DeviceId = device, DeviceName = "Phone",
        Date = day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), TimeSpan = TimeSpan.FromMinutes(minutes)
    };

    [Fact]
    public void Limit_CountsCompletedDaysInARow_NotTheRunningDay()
    {
        var goal = new GoalSettings { Scope = GoalScope.Total, Period = GoalPeriod.Day, Direction = GoalDirection.AtMost, Minutes = 60, Since = s_Today.AddDays(-10) };
        UsageDto[] usage =
        [
            Use(s_Today.AddDays(-5), 90), // missed
            Use(s_Today.AddDays(-4), 30), Use(s_Today.AddDays(-3), 59), Use(s_Today.AddDays(-2), 60), Use(s_Today.AddDays(-1), 10),
            Use(s_Today, 20)
        ];

        var progress = GoalEvaluator.Evaluate(goal, usage, s_Zone, s_Today);

        progress.Streak.ShouldBe(4);
        progress.Current.ShouldBe(TimeSpan.FromMinutes(20));
        progress.OnTrack.ShouldBeTrue();
        // Days before the miss without any usage met the limit too.
        progress.BestStreak.ShouldBe(5);
        progress.History[^1].Start.ShouldBe(s_Today);
    }

    [Fact]
    public void Target_ReachedToday_ExtendsTheStreakAlready()
    {
        var goal = new GoalSettings { Scope = GoalScope.Category, Target = "reading", Period = GoalPeriod.Day, Direction = GoalDirection.AtLeast, Minutes = 30, Since = s_Today.AddDays(-3) };
        UsageDto[] usage =
        [
            Use(s_Today.AddDays(-2), 40, "Kindle", "Reading"), Use(s_Today.AddDays(-1), 35, "Kindle", "Reading"),
            Use(s_Today, 31, "Kindle", "Reading"), Use(s_Today, 300) // other category, ignored
        ];

        var progress = GoalEvaluator.Evaluate(goal, usage, s_Zone, s_Today);

        progress.Current.ShouldBe(TimeSpan.FromMinutes(31));
        progress.Streak.ShouldBe(3);
        progress.History.First().Met.ShouldBeFalse(); // the day the goal started, nothing read
    }

    [Fact]
    public void WeeklyAppLimit_GroupsByIsoWeek()
    {
        var goal = new GoalSettings { Scope = GoalScope.App, Target = "Instagram", Period = GoalPeriod.Week, Direction = GoalDirection.AtMost, Minutes = 120, Since = s_Today.AddDays(-21) };
        UsageDto[] usage =
        [
            Use(s_Today.AddDays(-14), 100), Use(s_Today.AddDays(-8), 30), // week of Nov 9: 130 > 120
            Use(s_Today.AddDays(-7), 60), Use(s_Today.AddDays(-1), 60), // week of Nov 16: 120
            Use(s_Today, 200, app: "Firefox")
        ];

        var progress = GoalEvaluator.Evaluate(goal, usage, s_Zone, s_Today);

        progress.History.Select(h => h.Start).ShouldBe([new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 16), s_Today]);
        progress.Streak.ShouldBe(1);
        progress.Current.ShouldBe(TimeSpan.Zero);
        GoalEvaluator.Title(goal).ShouldBe("At most 2 h 0 min a week of Instagram");
    }

    [Fact]
    public void DeviceGoal_OnlyCountsThatDevice()
    {
        var goal = new GoalSettings { Scope = GoalScope.Device, Target = "2", Period = GoalPeriod.Day, Minutes = 10, Since = s_Today };
        var progress = GoalEvaluator.Evaluate(goal, [Use(s_Today, 5, device: 2), Use(s_Today, 50, device: 1)], s_Zone, s_Today,
            new Dictionary<int, string> { [2] = "Laptop" });
        progress.Current.ShouldBe(TimeSpan.FromMinutes(5));
        progress.Title.ShouldBe("At most 10 min a day on Laptop");
    }
}
