using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Dashboard;

public enum DashboardRange
{
    Day,
    Week,
    Month
}

/// <param name="Opens">How often the app was brought to the front (zero for data recorded before opens were
/// counted).</param>
public record AppTotal(int AppId, string Name, string? Category, TimeSpan Time, double Share, int Opens = 0);

/// <param name="Opens">For websites: how often they were opened.</param>
public record NamedTotal(string Name, TimeSpan Time, double Share, int Opens = 0);

/// <summary>A chart series: one value (hours) per period.</summary>
public record SeriesData(string Name, double[] Hours);

/// <summary>
/// Aggregates hourly usage buckets for the dashboard in the viewer's time zone.
/// </summary>
public sealed class UsageSummary
{
    /// <summary>Apps beyond this rank are combined into "Other" in the chart.</summary>
    public const int ChartApps = 5;

    public required DateTime LocalStart { get; init; }
    public required DateTime LocalEnd { get; init; }
    public required DashboardRange Range { get; init; }
    public TimeSpan Total { get; private init; }
    public IReadOnlyList<AppTotal> Apps { get; private init; } = [];
    public IReadOnlyList<NamedTotal> Categories { get; private init; } = [];
    public IReadOnlyList<NamedTotal> Devices { get; private init; } = [];

    /// <summary>Time per website (browsers on devices that record websites). Shares are of <see cref="WebsiteTime"/>.</summary>
    public IReadOnlyList<NamedTotal> Websites { get; private init; } = [];

    /// <summary>Browser time with a known website.</summary>
    public TimeSpan WebsiteTime { get; private init; }

    /// <summary>How often an app was brought to the front.</summary>
    public int Opens { get; private init; }

    /// <summary>Device unlocks (devices that record them, i.e. Android).</summary>
    public int Unlocks { get; private init; }
    public string[] PeriodLabels { get; private init; } = [];
    public IReadOnlyList<SeriesData> Series { get; private init; } = [];

    /// <summary>Local start and end (exclusive) of the period containing <paramref name="anchor"/>.</summary>
    public static (DateTime Start, DateTime End) Period(DateTime anchor, DashboardRange range)
    {
        var day = anchor.Date;
        return range switch
        {
            DashboardRange.Day => (day, day.AddDays(1)),
            // Weeks start on Monday.
            DashboardRange.Week => (day.AddDays(-(((int)day.DayOfWeek + 6) % 7)), day.AddDays(-(((int)day.DayOfWeek + 6) % 7) + 7)),
            _ => (new DateTime(day.Year, day.Month, 1), new DateTime(day.Year, day.Month, 1).AddMonths(1))
        };
    }

    /// <summary>UTC range to query for a local period. Hour buckets are UTC; zones with a half-hour offset
    /// get the overlapping buckets.</summary>
    public static (DateTime Start, DateTime End) ToUtc(DateTime localStart, DateTime localEnd, TimeZoneInfo zone) =>
        (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), zone));

    public static UsageSummary Create(IReadOnlyList<UsageDto> usage, DateTime localStart, DateTime localEnd,
        DashboardRange range, TimeZoneInfo zone, IReadOnlyList<DeviceUnlocks>? unlocks = null)
    {
        bool InPeriod(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            return local >= localStart && local < localEnd;
        }

        var rows = usage
            .Select(u => (Usage: u, Local: TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u.Date, DateTimeKind.Utc), zone)))
            .Where(r => r.Local >= localStart && r.Local < localEnd)
            .ToList();
        var unlockRows = (unlocks ?? []).Where(u => InPeriod(u.Hour)).ToList();

        var total = Sum(rows.Select(r => r.Usage.TimeSpan));
        double ShareOf(TimeSpan t) => total > TimeSpan.Zero ? t / total : 0;

        var apps = rows
            .GroupBy(r => r.Usage.AppId)
            .Select(g => new
            {
                First = g.First().Usage, Time = Sum(g.Select(r => r.Usage.TimeSpan)),
                Opens = g.Where(r => r.Usage.Domain is null).Sum(r => r.Usage.Opens)
            })
            .OrderByDescending(a => a.Time)
            .Select(a => new AppTotal(a.First.AppId, a.First.AppName, a.First.Category, a.Time, ShareOf(a.Time), a.Opens))
            .ToList();

        var categories = rows
            .GroupBy(r => r.Usage.Category ?? "Uncategorized")
            .Select(g => new NamedTotal(g.Key, Sum(g.Select(r => r.Usage.TimeSpan)), 0))
            .OrderByDescending(c => c.Time)
            .Select(c => c with { Share = ShareOf(c.Time) })
            .ToList();

        var devices = rows
            .GroupBy(r => r.Usage.DeviceName)
            .Select(g => new NamedTotal(g.Key, Sum(g.Select(r => r.Usage.TimeSpan)), 0))
            .OrderByDescending(d => d.Time)
            .Select(d => d with { Share = ShareOf(d.Time) })
            .ToList();

        var websiteRows = rows.Where(r => r.Usage.Domain is not null).ToList();
        var websiteTime = Sum(websiteRows.Select(r => r.Usage.TimeSpan));
        var websites = websiteRows
            .GroupBy(r => r.Usage.Domain!)
            .Select(g => new NamedTotal(g.Key, Sum(g.Select(r => r.Usage.TimeSpan)), 0, g.Sum(r => r.Usage.Opens)))
            .OrderByDescending(w => w.Time)
            .Select(w => w with { Share = websiteTime > TimeSpan.Zero ? w.Time / websiteTime : 0 })
            .ToList();

        // Day view: one column per hour. Week and month: one column per day.
        var byHour = range == DashboardRange.Day;
        var periods = byHour ? 24 : (int)(localEnd - localStart).TotalDays;
        int PeriodOf(DateTime local) => byHour ? local.Hour : (int)(local.Date - localStart.Date).TotalDays;

        var labels = Enumerable.Range(0, periods)
            .Select(i => byHour
                ? i % 3 == 0 ? $"{i:00}" : ""
                : range == DashboardRange.Week
                    ? localStart.AddDays(i).ToString("ddd")
                    : (i % 5 == 0 ? localStart.AddDays(i).ToString("dd") : ""))
            .ToArray();

        var chartApps = apps.Take(ChartApps).Select(a => a.AppId).ToHashSet();
        var series = apps.Take(ChartApps)
            .Select(a => new SeriesData(a.Name, new double[periods]))
            .ToList();
        var other = new SeriesData("Other", new double[periods]);
        var seriesByApp = apps.Take(ChartApps).Zip(series).ToDictionary(p => p.First.AppId, p => p.Second);

        foreach (var (u, local) in rows)
        {
            var period = PeriodOf(local);
            if (period < 0 || period >= periods)
                continue;
            var target = chartApps.Contains(u.AppId) ? seriesByApp[u.AppId] : other;
            target.Hours[period] += u.TimeSpan.TotalHours;
        }

        if (other.Hours.Any(h => h > 0))
            series.Add(other);

        return new UsageSummary
        {
            LocalStart = localStart,
            LocalEnd = localEnd,
            Range = range,
            Total = total,
            Apps = apps,
            Categories = categories,
            Devices = devices,
            Websites = websites,
            WebsiteTime = websiteTime,
            Opens = apps.Sum(a => a.Opens),
            Unlocks = unlockRows.Sum(u => u.Count),
            PeriodLabels = labels,
            Series = series
        };
    }

    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{Math.Max(time.Minutes, time > TimeSpan.Zero ? 1 : 0)} min";

    private static TimeSpan Sum(IEnumerable<TimeSpan> times) => times.Aggregate(TimeSpan.Zero, (a, b) => a + b);
}
