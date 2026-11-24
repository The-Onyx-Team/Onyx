using System.Text.Json.Serialization;
using Onyx.Collector.Core.Crypto;

namespace Onyx.Collector.Core.Reminders;

/// <summary>A stretch of time an app was in front on this device.</summary>
public sealed record ActivityInterval(ForegroundApp App, DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>A notification to show.</summary>
/// <param name="Key">Identifies what it is about; showing a new one with the same key may replace the old one.</param>
public sealed record Nudge(string Key, string Title, string Message);

/// <summary>What was shown when, so nothing is shown twice. Kept by the host (in memory, or saved between runs of a
/// background job on Android).</summary>
public sealed class ReminderState
{
    [JsonPropertyName("shown")]
    public Dictionary<string, DateTimeOffset> Shown { get; set; } = new();
}

/// <summary>
/// Decides which reminders and focus-mode nudges are due, from what this device knows: its recent foreground
/// activity and the user's settings. Runs on every collector (Android, Windows, Linux); the server is not involved.
/// </summary>
public static class ReminderEngine
{
    /// <summary>A pause at least this long ends a stretch of continuous use.</summary>
    public static readonly TimeSpan BreakLength = TimeSpan.FromMinutes(5);

    /// <summary>How often focus mode nudges about the same app while it stays in front.</summary>
    public static readonly TimeSpan FocusRepeat = TimeSpan.FromMinutes(10);

    /// <summary>An interval ending this close to now counts as the app being in front now.</summary>
    public static readonly TimeSpan CurrentTolerance = TimeSpan.FromMinutes(1);

    /// <param name="deviceId">This device's id (rules can be limited to devices); null if not registered.</param>
    /// <param name="recent">This device's activity, at least since the start of the local day, oldest first.</param>
    public static List<Nudge> Evaluate(VaultSettings settings, int? deviceId, IReadOnlyList<ActivityInterval> recent,
        DateTimeOffset now, TimeZoneInfo zone, ReminderState state)
    {
        var nudges = new List<Nudge>();
        string? Category(ForegroundApp app) => settings.CategoryOf(app.Identifier, app.Category);

        foreach (var rule in settings.Reminders)
        {
            if (!rule.Enabled || rule.Minutes <= 0 || (rule.Devices.Count > 0 && (deviceId is null || !rule.Devices.Contains(deviceId.Value))))
                continue;

            var matching = recent.Where(i => rule.Apps.Matches(i.App.Identifier, Category(i.App), emptyMatchesAll: true))
                .ToList();
            var limit = TimeSpan.FromMinutes(rule.Minutes);

            if (rule.Kind == ReminderKind.Continuous)
            {
                if (CurrentStretch(matching, now) is not { } stretch)
                    continue;
                var length = now - stretch.Start;
                var key = $"r|{rule.Id}";
                var last = state.Shown.GetValueOrDefault(key);
                // Once the limit is reached, and again every as long, during the same stretch.
                if (length >= limit && (last < stretch.Start || now - last >= limit))
                {
                    state.Shown[key] = now;
                    nudges.Add(new Nudge(key, "Time for a break?",
                        rule.Message ?? $"You have been using {Describe(rule.Apps, stretch.Apps)} for {Format(length)} without a break."));
                }
            }
            else
            {
                var localNow = TimeZoneInfo.ConvertTime(now, zone);
                var dayStart = new DateTimeOffset(localNow.Date, localNow.Offset);
                var today = matching.Sum(i => Overlap(i, dayStart, now).TotalSeconds);
                var key = $"r|{rule.Id}|{localNow:yyyy-MM-dd}";
                if (today >= limit.TotalSeconds && !state.Shown.ContainsKey(key))
                {
                    state.Shown[key] = now;
                    var apps = matching.Select(i => i.App).ToList();
                    nudges.Add(new Nudge(key, "Daily limit reached",
                        rule.Message ?? $"{Format(TimeSpan.FromSeconds(today))} today on {Describe(rule.Apps, apps)} on this device."));
                }
            }
        }

        var focus = settings.Focus;
        if (focus.AppliesTo(deviceId) && focus.ActiveUntil(now, zone) is { } until
            && recent.LastOrDefault() is { } latest && now - latest.End <= CurrentTolerance
            && focus.Distracting.Matches(latest.App.Identifier, Category(latest.App), emptyMatchesAll: false))
        {
            var key = $"f|{latest.App.Identifier}";
            if (now - state.Shown.GetValueOrDefault(key) >= FocusRepeat)
            {
                state.Shown[key] = now;
                var end = TimeZoneInfo.ConvertTime(until, zone);
                nudges.Add(new Nudge(key, "Focus mode",
                    $"{latest.App.Name} is on your list of distractions. Focus mode is on until {end:HH:mm}."));
            }
        }

        // Forget what is too old to matter.
        foreach (var old in state.Shown.Where(s => now - s.Value > TimeSpan.FromDays(2)).Select(s => s.Key).ToList())
            state.Shown.Remove(old);

        return nudges;
    }

    /// <summary>The stretch of use (pauses shorter than <see cref="BreakLength"/> bridged) that is still going on.</summary>
    internal static (DateTimeOffset Start, List<ForegroundApp> Apps)? CurrentStretch(IReadOnlyList<ActivityInterval> intervals,
        DateTimeOffset now)
    {
        var ordered = intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start).ToList();
        if (ordered.Count == 0 || now - ordered.Max(i => i.End) > CurrentTolerance)
            return null;

        var start = ordered[^1].Start;
        var end = ordered.Max(i => i.End);
        var apps = new List<ForegroundApp>();
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (start - ordered[i].End >= BreakLength)
                break;
            start = ordered[i].Start < start ? ordered[i].Start : start;
            apps.Add(ordered[i].App);
        }

        return end >= now - CurrentTolerance ? (start, apps) : null;
    }

    private static TimeSpan Overlap(ActivityInterval interval, DateTimeOffset from, DateTimeOffset to)
    {
        var start = interval.Start > from ? interval.Start : from;
        var end = interval.End < to ? interval.End : to;
        return end > start ? end - start : TimeSpan.Zero;
    }

    /// <summary>"YouTube", "YouTube and Instagram", "Social apps", or "your devices".</summary>
    private static string Describe(AppSelection selection, IReadOnlyList<ForegroundApp> used)
    {
        if (selection.IsEmpty)
            return "this device";
        var names = used.Select(a => a.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(3).ToList();
        if (selection.Apps.Count == 0 && selection.Categories.Count == 1)
            return $"{selection.Categories[0]} apps";
        return names.Count switch
        {
            0 => "these apps",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]}, {names[1]} and others"
        };
    }

    public static string Format(TimeSpan time)
    {
        var minutes = (int)Math.Round(time.TotalMinutes);
        return minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }
}

/// <summary>
/// The recent foreground activity of a desktop device, for <see cref="ReminderEngine"/>. The tracker adds every
/// sample; adjacent samples of the same app become one interval. Keeps the last 26 hours (a whole local day).
/// </summary>
public sealed class ActivityLog
{
    private static readonly TimeSpan s_Keep = TimeSpan.FromHours(26);
    private readonly List<ActivityInterval> m_Intervals = [];
    private readonly Lock m_Lock = new();

    public void Add(ForegroundApp app, DateTimeOffset start, DateTimeOffset end)
    {
        lock (m_Lock)
        {
            if (m_Intervals.Count > 0 && m_Intervals[^1] is var last && last.App.Identifier == app.Identifier
                && start - last.End < TimeSpan.FromSeconds(10))
                m_Intervals[^1] = last with { End = end };
            else
                m_Intervals.Add(new ActivityInterval(app, start, end));

            var cutoff = end - s_Keep;
            m_Intervals.RemoveAll(i => i.End < cutoff);
        }
    }

    public IReadOnlyList<ActivityInterval> Snapshot()
    {
        lock (m_Lock)
            return m_Intervals.ToList();
    }
}
