using Onyx.App.Shared.Services.Dashboard;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Goals;

/// <summary>One period (day or week) of a goal.</summary>
public sealed record GoalPeriodResult(DateOnly Start, TimeSpan Time, bool Met);

/// <summary>Where a goal stands.</summary>
/// <param name="Current">Time in the current (running) period.</param>
/// <param name="OnTrack">Limits: still below. Targets: reached already.</param>
/// <param name="Streak">Completed periods in a row that met the goal, up to the last completed one; a target
/// already reached in the running period counts too.</param>
/// <param name="History">Completed periods, oldest first, plus the running one last.</param>
public sealed record GoalProgress(
    GoalSettings Goal,
    string Title,
    TimeSpan Current,
    TimeSpan Target,
    bool OnTrack,
    int Streak,
    int BestStreak,
    IReadOnlyList<GoalPeriodResult> History)
{
    /// <summary>Share of the target used (limits) or reached (targets), for a progress bar.</summary>
    public double Ratio => Target > TimeSpan.Zero ? Current / Target : 0;
}

/// <summary>Evaluates goals and streaks from decrypted usage, in the viewer's time zone.</summary>
public static class GoalEvaluator
{
    /// <summary>How far back streaks are counted.</summary>
    public const int HistoryDays = 120;

    public static GoalProgress Evaluate(GoalSettings goal, IReadOnlyList<UsageDto> usage, TimeZoneInfo zone,
        DateOnly today, IReadOnlyDictionary<int, string>? deviceNames = null)
    {
        var target = TimeSpan.FromMinutes(goal.Minutes);
        var byPeriod = usage
            .Where(u => Matches(goal, u))
            .GroupBy(u => PeriodOf(goal, DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u.Date, DateTimeKind.Utc), zone))))
            .ToDictionary(g => g.Key, g => g.Aggregate(TimeSpan.Zero, (t, u) => t + u.TimeSpan));

        var current = PeriodOf(goal, today);
        var first = PeriodOf(goal, goal.Since > today.AddDays(-HistoryDays) ? goal.Since : today.AddDays(-HistoryDays));
        var history = new List<GoalPeriodResult>();
        for (var start = first; start <= current; start = Next(goal, start))
        {
            var time = byPeriod.GetValueOrDefault(start);
            history.Add(new GoalPeriodResult(start, time, IsMet(goal, time, target)));
        }

        var completed = history.Take(history.Count - 1).ToList();
        var running = history[^1];

        var streak = 0;
        for (var i = completed.Count - 1; i >= 0 && completed[i].Met; i--)
            streak++;
        // A target reached today can't be lost any more; a limit can, so it only counts once the day is over.
        if (goal.Direction == GoalDirection.AtLeast && running.Met)
            streak++;

        int best = 0, run = 0;
        foreach (var period in goal.Direction == GoalDirection.AtLeast ? history : completed)
        {
            run = period.Met ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return new GoalProgress(goal, Title(goal, deviceNames), running.Time, target,
            IsMet(goal, running.Time, target), streak, Math.Max(best, streak), history);
    }

    public static string Title(GoalSettings goal, IReadOnlyDictionary<int, string>? deviceNames = null)
    {
        var amount = UsageSummary.Format(TimeSpan.FromMinutes(goal.Minutes));
        var per = goal.Period == GoalPeriod.Day ? "a day" : "a week";
        var what = goal.Scope switch
        {
            GoalScope.Total => "on all devices",
            GoalScope.Device => int.TryParse(goal.Target, out var id) && deviceNames?.GetValueOrDefault(id) is { } name
                ? $"on {name}"
                : "on one device",
            GoalScope.Category => $"of {goal.Target}",
            _ => $"of {goal.Target}"
        };
        return goal.Direction == GoalDirection.AtMost
            ? $"At most {amount} {per} {what}"
            : $"At least {amount} {per} {what}";
    }

    public static bool Matches(GoalSettings goal, UsageDto usage) => goal.Scope switch
    {
        GoalScope.Total => true,
        GoalScope.Device => int.TryParse(goal.Target, out var id) && usage.DeviceId == id,
        GoalScope.Category => string.Equals(usage.Category, goal.Target, StringComparison.CurrentCultureIgnoreCase),
        GoalScope.App => string.Equals(usage.AppName, goal.Target, StringComparison.CurrentCultureIgnoreCase),
        _ => false
    };

    private static bool IsMet(GoalSettings goal, TimeSpan time, TimeSpan target) =>
        goal.Direction == GoalDirection.AtMost ? time <= target : time >= target;

    private static DateOnly PeriodOf(GoalSettings goal, DateOnly day) =>
        goal.Period == GoalPeriod.Day ? day : Weeks.StartOf(day);

    private static DateOnly Next(GoalSettings goal, DateOnly start) =>
        start.AddDays(goal.Period == GoalPeriod.Day ? 1 : 7);
}
