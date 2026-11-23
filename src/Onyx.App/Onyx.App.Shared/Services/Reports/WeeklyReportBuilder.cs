using System.Globalization;
using System.Text.Json.Serialization;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>
/// Turns hourly usage rows into a <see cref="WeeklyReportDto"/> and the data the chart script plots. Weeks run
/// Monday to Sunday in the viewer's time zone.
/// </summary>
public static class WeeklyReportBuilder
{
    public const int TopApps = 10;
    public const int TopAppsPerDevice = 5;

    /// <summary>First instant (UTC) whose usage the report needs: the start of the oldest trend week.</summary>
    public static (DateTime Start, DateTime End) UtcRange(DateOnly weekStart, TimeZoneInfo zone)
    {
        var first = weekStart.AddDays(-7 * (WeeklyReportDto.TrendWeeks - 1));
        return (ToUtc(first, zone), ToUtc(weekStart.AddDays(7), zone));
    }

    /// <summary>The Monday of the week that contains <paramref name="day"/>.</summary>
    public static DateOnly WeekStartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <param name="rows">Usage from <see cref="UtcRange"/>, with UTC bucket starts.</param>
    /// <param name="today">Today in <paramref name="zone"/>, for the daily average of the running week.</param>
    public static (WeeklyReportDto Report, ChartInput Charts) Build(IReadOnlyList<UsageDto> rows, DateOnly weekStart,
        TimeZoneInfo zone, DateOnly today)
    {
        var local = rows
            .Select(r => (Row: r, Time: TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.Date, DateTimeKind.Utc), zone)))
            .Select(x => (x.Row, x.Time, Day: DateOnly.FromDateTime(x.Time)))
            .ToList();

        var week = local.Where(x => x.Day >= weekStart && x.Day < weekStart.AddDays(7)).ToList();
        var previous = local.Where(x => x.Day >= weekStart.AddDays(-7) && x.Day < weekStart).ToList();

        var dayLabels = Enumerable.Range(0, 7)
            .Select(i => weekStart.AddDays(i).ToString("ddd dd.MM.", CultureInfo.InvariantCulture))
            .ToArray();

        TimeSpan Sum(IEnumerable<(UsageDto Row, DateTime Time, DateOnly Day)> items) =>
            items.Aggregate(TimeSpan.Zero, (t, x) => t + x.Row.TimeSpan);

        List<ReportItemDto> Ranked(IEnumerable<(UsageDto Row, DateTime Time, DateOnly Day)> current,
            IEnumerable<(UsageDto Row, DateTime Time, DateOnly Day)> before, Func<UsageDto, string> key, int take)
        {
            var previousByKey = before.GroupBy(x => key(x.Row)).ToDictionary(g => g.Key, g => Sum(g));
            return current.GroupBy(x => key(x.Row))
                .Select(g => new ReportItemDto(g.Key, Sum(g), previousByKey.GetValueOrDefault(g.Key)))
                .OrderByDescending(i => i.Time)
                .Take(take)
                .ToList();
        }

        var total = Sum(week);
        var daily = Enumerable.Range(0, 7).Select(i => Sum(week.Where(x => x.Day == weekStart.AddDays(i)))).ToArray();
        var busiest = Array.IndexOf(daily, daily.Max());

        var elapsedDays = Math.Clamp(today.DayNumber - weekStart.DayNumber + 1, 1, 7);

        var devices = week.GroupBy(x => (x.Row.DeviceId, x.Row.DeviceName))
            .Select(g => new ReportDeviceDto(g.Key.DeviceId, g.Key.DeviceName, Sum(g),
                Sum(previous.Where(x => x.Row.DeviceId == g.Key.DeviceId)),
                Ranked(g, [], r => r.AppName, TopAppsPerDevice)))
            .OrderByDescending(d => d.Time)
            .ToList();

        var trendWeeks = Enumerable.Range(0, WeeklyReportDto.TrendWeeks)
            .Select(i => weekStart.AddDays(-7 * (WeeklyReportDto.TrendWeeks - 1 - i)))
            .ToList();
        var trend = trendWeeks
            .Select(start => new ReportItemDto(start.ToString("dd.MM.", CultureInfo.InvariantCulture),
                Sum(local.Where(x => x.Day >= start && x.Day < start.AddDays(7))), TimeSpan.Zero))
            .ToList();

        var report = new WeeklyReportDto
        {
            WeekStart = weekStart,
            Total = total,
            PreviousTotal = Sum(previous),
            DailyAverage = total / elapsedDays,
            BusiestDay = total > TimeSpan.Zero ? weekStart.AddDays(busiest).ToString("dddd", CultureInfo.InvariantCulture) : null,
            BusiestDayTime = daily[busiest],
            Apps = Ranked(week, previous, r => r.AppName, TopApps),
            Categories = Ranked(week, previous, r => r.Category ?? "Uncategorized", int.MaxValue),
            Devices = devices,
            Trend = trend,
        };

        var labels = Labels(local.Select(x => (x.Row.DeviceId, x.Row.DeviceName)));
        var charts = new ChartInput
        {
            Days = dayLabels,
            DaysShort = Enumerable.Range(0, 7)
                .Select(i => weekStart.AddDays(i).ToString("ddd", CultureInfo.InvariantCulture))
                .ToArray(),
            DailyByDevice = devices.ToDictionary(d => labels[d.Id],
                d => Enumerable.Range(0, 7)
                    .Select(i => Hours(Sum(week.Where(x => x.Row.DeviceId == d.Id && x.Day == weekStart.AddDays(i)))))
                    .ToArray()),
            Apps = report.Apps.Select(a => new ChartApp(a.Name, Hours(a.Time), Hours(a.PreviousTime))).ToList(),
            Categories = report.Categories.Select(c => new ChartApp(c.Name, Hours(c.Time), Hours(c.PreviousTime))).ToList(),
            Trend = new ChartTrend
            {
                Labels = trend.Select(t => t.Name).ToArray(),
                Total = trend.Select(t => Hours(t.Time)).ToArray(),
                Devices = labels.Count > 1
                    ? labels.ToDictionary(d => d.Value, d => trendWeeks
                        .Select(start => Hours(Sum(local.Where(x =>
                            x.Row.DeviceId == d.Key && x.Day >= start && x.Day < start.AddDays(7)))))
                        .ToArray())
                    : [],
            },
            Heatmap = Enumerable.Range(0, 7)
                .Select(day => Enumerable.Range(0, 24)
                    .Select(hour => Math.Round(Sum(week.Where(x => x.Day == weekStart.AddDays(day) && x.Time.Hour == hour))
                        .TotalMinutes, 1))
                    .ToArray())
                .ToArray(),
            Devices = devices.Select(d => new ChartDevice(
                    $"device-{d.Id}",
                    Enumerable.Range(0, 7)
                        .Select(i => Hours(Sum(week.Where(x => x.Row.DeviceId == d.Id && x.Day == weekStart.AddDays(i)))))
                        .ToArray(),
                    d.Apps.Select(a => new ChartApp(a.Name, Hours(a.Time), 0)).ToList()))
                .ToList(),
        };

        return (report, charts);
    }

    private static double Hours(TimeSpan time) => Math.Round(time.TotalHours, 3);

    /// <remarks>In zones that change the clock at midnight the day starts at 01:00.</remarks>
    private static DateTime ToUtc(DateOnly day, TimeZoneInfo zone)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(midnight))
            midnight = midnight.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }

    /// <summary>Device names for chart legends; devices with the same name get their id appended.</summary>
    private static Dictionary<int, string> Labels(IEnumerable<(int Id, string Name)> devices)
    {
        var list = devices.DistinctBy(d => d.Id).ToList();
        var duplicates = list.GroupBy(d => d.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return list.ToDictionary(d => d.Id, d => duplicates.Contains(d.Name) ? $"{d.Name} (#{d.Id})" : d.Name);
    }
}

/// <summary>Input of <c>Reports/weekly_report.py</c> (snake_case JSON). Times are in hours unless noted.</summary>
public sealed class ChartInput
{
    [JsonPropertyName("days")] public string[] Days { get; init; } = [];
    [JsonPropertyName("days_short")] public string[] DaysShort { get; init; } = [];
    [JsonPropertyName("daily_by_device")] public Dictionary<string, double[]> DailyByDevice { get; init; } = [];
    [JsonPropertyName("apps")] public List<ChartApp> Apps { get; init; } = [];
    [JsonPropertyName("categories")] public List<ChartApp> Categories { get; init; } = [];
    [JsonPropertyName("trend")] public ChartTrend Trend { get; init; } = new();

    /// <summary>Minutes per weekday (rows) and hour of the day (columns).</summary>
    [JsonPropertyName("heatmap")] public double[][] Heatmap { get; init; } = [];

    [JsonPropertyName("devices")] public List<ChartDevice> Devices { get; init; } = [];
}

public sealed record ChartApp(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("hours")] double Hours,
    [property: JsonPropertyName("previous_hours")] double PreviousHours);

public sealed class ChartTrend
{
    [JsonPropertyName("labels")] public string[] Labels { get; init; } = [];
    [JsonPropertyName("total")] public double[] Total { get; init; } = [];
    [JsonPropertyName("devices")] public Dictionary<string, double[]> Devices { get; init; } = [];
}

public sealed record ChartDevice(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("daily")] double[] Daily,
    [property: JsonPropertyName("apps")] List<ChartApp> Apps);
