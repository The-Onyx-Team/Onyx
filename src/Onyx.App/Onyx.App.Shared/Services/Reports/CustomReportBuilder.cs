using System.Globalization;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Documents;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>
/// Builds a report the user put together (<see cref="CustomReportSettings"/>): any period, only some devices, apps
/// or categories, and the sections they picked. Compared with the period of the same length just before.
/// </summary>
public static class CustomReportBuilder
{
    public const int TopApps = 10;
    public const int TopWebsites = 10;
    private const int TrendMinDays = 21;

    /// <summary>The UTC range of usage the report needs: the period and the one before (for comparison).</summary>
    public static (DateTime Start, DateTime End) UtcRange(DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return (ToUtc(from.AddDays(-days), zone), ToUtc(to.AddDays(1), zone));
    }

    /// <param name="rows">Usage from <see cref="UtcRange"/>.</param>
    /// <param name="appNames">Names of the apps the report is limited to (from its selection); null: all apps.</param>
    public static ReportLayout Build(CustomReportSettings report, IReadOnlyList<UsageDto> rows, DateOnly from,
        DateOnly to, TimeZoneInfo zone, IReadOnlySet<string>? appNames,
        IReadOnlyDictionary<string, string>? categoryColors = null)
    {
        var dayCount = to.DayNumber - from.DayNumber + 1;
        var before = from.AddDays(-dayCount);

        bool Included(UsageDto row) =>
            (report.Devices.Count == 0 || report.Devices.Contains(row.DeviceId))
            && (report.Apps.IsEmpty
                || (appNames?.Contains(row.AppName) ?? false)
                || (row.Category is { } c && report.Apps.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)));

        var local = rows.Where(Included)
            .Select(r => (Row: r, Time: TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.Date, DateTimeKind.Utc), zone)))
            .Select(x => (x.Row, x.Time, Day: DateOnly.FromDateTime(x.Time)))
            .ToList();
        var period = local.Where(x => x.Day >= from && x.Day <= to).ToList();
        var previous = local.Where(x => x.Day >= before && x.Day < from).ToList();

        TimeSpan Sum(IEnumerable<(UsageDto Row, DateTime Time, DateOnly Day)> items) =>
            items.Aggregate(TimeSpan.Zero, (t, x) => t + x.Row.TimeSpan);
        static double Hours(TimeSpan time) => Math.Round(time.TotalHours, 3);

        List<ReportItemDto> Ranked(Func<UsageDto, string?> key, int take, IEnumerable<(UsageDto Row, DateTime Time, DateOnly Day)>? current = null)
        {
            var earlier = previous.Where(x => key(x.Row) is not null).GroupBy(x => key(x.Row)!).ToDictionary(g => g.Key, g => Sum(g));
            return (current ?? period).Where(x => key(x.Row) is not null).GroupBy(x => key(x.Row)!)
                .Select(g => new ReportItemDto(g.Key, Sum(g), earlier.GetValueOrDefault(g.Key)))
                .OrderByDescending(i => i.Time)
                .Take(take)
                .ToList();
        }

        var days = Enumerable.Range(0, dayCount).Select(i => from.AddDays(i)).ToList();
        var labels = days.Select(d => d.ToString(dayCount <= 14 ? "ddd dd.MM." : "dd.MM.", CultureInfo.InvariantCulture)).ToArray();
        var daily = days.Select(d => Sum(period.Where(x => x.Day == d))).ToArray();
        var total = Sum(period);
        var busiest = daily.Length == 0 ? 0 : Array.IndexOf(daily, daily.Max());
        var apps = Ranked(r => r.AppName, TopApps);
        var categories = Ranked(r => r.Category ?? "Uncategorized", int.MaxValue);
        var websites = Ranked(r => r.Domain, TopWebsites);

        var devices = period.GroupBy(x => (x.Row.DeviceId, x.Row.DeviceName))
            .Select(g => new ReportDeviceDto(g.Key.DeviceId, g.Key.DeviceName, Sum(g),
                Sum(previous.Where(x => x.Row.DeviceId == g.Key.DeviceId)),
                Ranked(r => r.AppName, 5, g)))
            .OrderByDescending(d => d.Time)
            .ToList();
        var deviceLabels = devices.GroupBy(d => d.Name)
            .SelectMany(g => g.Count() == 1 ? g.Select(d => (d.Id, d.Name)) : g.Select(d => (d.Id, $"{d.Name} (#{d.Id})")))
            .ToDictionary(d => d.Id, d => d.Item2);

        // Weeks of the period, for the trend (only for periods of three weeks or more).
        var weeks = new List<DateOnly>();
        for (var week = WeeklyReportBuilder.WeekStartOf(from); week <= to; week = week.AddDays(7))
            weeks.Add(week);
        TimeSpan WeekSum(DateOnly week, int? deviceId = null) => Sum(period.Where(x =>
            x.Day >= week && x.Day < week.AddDays(7) && (deviceId is null || x.Row.DeviceId == deviceId)));

        // Average minutes per weekday and hour over the period.
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var heatmap = weekdays.Select(wd =>
        {
            var count = Math.Max(1, days.Count(d => d.DayOfWeek == wd));
            return Enumerable.Range(0, 24)
                .Select(h => Math.Round(Sum(period.Where(x => x.Day.DayOfWeek == wd && x.Time.Hour == h)).TotalMinutes / count, 1))
                .ToArray();
        }).ToArray();

        var input = new ChartInput
        {
            Days = labels,
            DaysShort = labels,
            DailyByDevice = devices.ToDictionary(d => deviceLabels[d.Id],
                d => days.Select(day => Hours(Sum(period.Where(x => x.Row.DeviceId == d.Id && x.Day == day)))).ToArray()),
            Apps = apps.Select(a => new ChartApp(a.Name, Hours(a.Time), Hours(a.PreviousTime))).ToList(),
            Categories = categories.Select(c => new ChartApp(c.Name, Hours(c.Time), Hours(c.PreviousTime))).ToList(),
            Trend = new ChartTrend
            {
                Labels = weeks.Select(w => w.ToString("dd.MM.", CultureInfo.InvariantCulture)).ToArray(),
                Total = weeks.Select(w => Hours(WeekSum(w))).ToArray(),
                Devices = devices.Count > 1
                    ? devices.ToDictionary(d => deviceLabels[d.Id], d => weeks.Select(w => Hours(WeekSum(w, d.Id))).ToArray())
                    : []
            },
            Heatmap = heatmap,
            Devices = devices.Select(d => new ChartDevice($"device-{d.Id}",
                days.Select(day => Hours(Sum(period.Where(x => x.Row.DeviceId == d.Id && x.Day == day)))).ToArray(),
                d.Apps.Select(a => new ChartApp(a.Name, Hours(a.Time), 0)).ToList())).ToList()
        };

        var charts = ReportCharts.Build(input, categoryColors);
        // The heatmap's rows are weekdays here, not dates.
        charts["heatmap"] = ReportCharts.HeatmapChart(weekdays.Select(w => w.ToString()).ToArray(), heatmap);
        if (websites.Count > 0)
            charts["websites"] = ReportCharts.Ranking(websites.Select(w => new ChartApp(w.Name, Hours(w.Time), Hours(w.PreviousTime))).ToList());
        if (dayCount < TrendMinDays)
            charts.Remove("trend");

        var periodText = from == to
            ? from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : $"{from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} – {to.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        var compared = $"the {dayCount} days before";

        List<ReportStat> stats = total == TimeSpan.Zero
            ? []
            :
            [
                new("Screen time", UsageSummary.Format(total), $"{ReportText.Change(total, Sum(previous))} vs. {compared}"),
                new("Per day", UsageSummary.Format(total / dayCount), $"on average over {dayCount} days"),
                new("Busiest day", days[busiest].ToString("ddd d MMM", CultureInfo.InvariantCulture), UsageSummary.Format(daily[busiest])),
                new("Most used", apps.FirstOrDefault()?.Name ?? "–", UsageSummary.Format(apps.FirstOrDefault()?.Time ?? TimeSpan.Zero))
            ];

        var blocks = new List<ReportBlock>();
        if (total == TimeSpan.Zero)
            blocks.Add(new TextBlock("No usage was recorded in this period for what the report includes."));
        else
        {
            var sections = report.Sections.ToHashSet();
            foreach (var (key, _) in CustomReportSettings.AllSections.Where(s => sections.Contains(s.Key)))
            {
                switch (key)
                {
                    case "daily":
                        blocks.Add(new ChartBlock("daily", "Every day", "Screen time per day, split by device."));
                        break;
                    case "apps":
                        blocks.Add(new ChartBlock("apps", "Top apps", $"Compared with {compared}."));
                        break;
                    case "websites":
                        blocks.Add(charts.ContainsKey("websites")
                            ? new ChartBlock("websites", "Top websites", $"Compared with {compared}. Only from devices that record websites.")
                            : new TextBlock("No websites were recorded in this period (only computers with the browser extension record them)."));
                        break;
                    case "categories" when sections.Contains("trend") && charts.ContainsKey("trend"):
                        blocks.Add(new SideBySideBlock(new ChartBlock("categories", "Categories"),
                            new ChartBlock("trend", "Weekly trend", "Screen time per week (labels: Monday of each week).")));
                        break;
                    case "categories":
                        blocks.Add(new ChartBlock("categories", "Categories"));
                        break;
                    case "trend" when sections.Contains("categories"):
                        break; // drawn next to the categories
                    case "trend":
                        blocks.Add(charts.ContainsKey("trend")
                            ? new ChartBlock("trend", "Weekly trend", "Screen time per week (labels: Monday of each week).")
                            : new TextBlock($"The weekly trend needs a period of at least {TrendMinDays} days."));
                        break;
                    case "heatmap":
                        blocks.Add(new ChartBlock("heatmap", "Time of day", "Average minutes of use per hour, by weekday."));
                        break;
                    case "devices" when devices.Count > 0:
                        blocks.Add(new HeadingBlock("Devices"));
                        blocks.AddRange(devices.Select(d => new DeviceBlock($"{d.Name}: {UsageSummary.Format(d.Time)}",
                            $"({ReportText.Change(d.Time, d.PreviousTime)} vs. {compared})", $"device-{d.Id}")));
                        break;
                }
            }
        }

        var title = string.IsNullOrWhiteSpace(report.Title) ? "Report" : report.Title.Trim();
        return new ReportLayout(title, periodText, stats, blocks, charts, $"Onyx report \"{title}\", {periodText}");
    }

    private static DateTime ToUtc(DateOnly day, TimeZoneInfo zone)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(midnight))
            midnight = midnight.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
