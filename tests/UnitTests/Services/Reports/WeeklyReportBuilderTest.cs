using JetBrains.Annotations;
using Onyx.App.Shared.Services.Reports;
using Onyx.Data.ApiSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Reports;

[TestSubject(typeof(WeeklyReportBuilder))]
public class WeeklyReportBuilderTest
{
    private static readonly TimeZoneInfo s_Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
    private static readonly DateOnly s_Monday = new(2026, 11, 16);

    private static UsageDto Row(DateTime utcHour, int minutes, string app = "Chat", string device = "Phone",
        int deviceId = 1, string? category = "Social") => new()
    {
        AppId = app.GetHashCode(), AppName = app, Category = category, Date = utcHour,
        TimeSpan = TimeSpan.FromMinutes(minutes), DeviceId = deviceId, DeviceName = device
    };

    private static DateTime Utc(int day, int hour) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WeekStartOf_ReturnsMonday()
    {
        WeeklyReportBuilder.WeekStartOf(new DateOnly(2026, 11, 22)).ShouldBe(s_Monday); // Sunday
        WeeklyReportBuilder.WeekStartOf(s_Monday).ShouldBe(s_Monday);
        WeeklyReportBuilder.WeekStartOf(new DateOnly(2026, 11, 18)).ShouldBe(s_Monday);
    }

    [Fact]
    public void UtcRange_CoversTheTrendWeeksInTheViewersZone()
    {
        var (start, end) = WeeklyReportBuilder.UtcRange(s_Monday, s_Vienna);

        // Vienna is UTC+1 in November: local midnight is 23:00 UTC the day before.
        end.ShouldBe(new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc));
        (end - start).TotalDays.ShouldBe(7 * WeeklyReportDto.TrendWeeks, tolerance: 1.0 / 24);
    }

    [Fact]
    public void Build_SumsTheWeekAndComparesWithTheWeekBefore()
    {
        var rows = new List<UsageDto>
        {
            Row(Utc(16, 8), 30),                  // Monday 09:00 local
            Row(Utc(16, 9), 60, "Mail", category: null),
            Row(Utc(18, 18), 45, "Chat", "Laptop", 2),
            Row(Utc(15, 23), 50),                 // Sunday 23:00 UTC = Monday 00:00 local: this week
            Row(Utc(15, 21), 40),                 // Sunday 22:00 local: the week before
            Row(Utc(10, 12), 20, "Mail"),         // the week before
        };

        var (report, charts) = WeeklyReportBuilder.Build(rows, s_Monday, s_Vienna, today: new DateOnly(2026, 11, 18));

        report.Total.ShouldBe(TimeSpan.FromMinutes(185));
        report.PreviousTotal.ShouldBe(TimeSpan.FromMinutes(60));
        report.DailyAverage.ShouldBe(TimeSpan.FromMinutes(185) / 3); // Monday to Wednesday so far
        report.BusiestDay.ShouldBe("Monday");

        report.Apps[0].ShouldBe(new ReportItemDto("Chat", TimeSpan.FromMinutes(125), TimeSpan.FromMinutes(40)));
        report.Apps[1].ShouldBe(new ReportItemDto("Mail", TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(20)));
        report.Categories.Select(c => c.Name).ShouldBe(["Social", "Uncategorized"]);

        report.Devices.Select(d => (d.Name, d.Time)).ShouldBe([("Phone", TimeSpan.FromMinutes(140)), ("Laptop", TimeSpan.FromMinutes(45))]);
        report.Trend.Count.ShouldBe(WeeklyReportDto.TrendWeeks);
        report.Trend[^1].Time.ShouldBe(report.Total);
        report.Trend[^2].Time.ShouldBe(report.PreviousTotal);

        charts.Days.Length.ShouldBe(7);
        charts.Heatmap[0][9].ShouldBe(30);  // Monday 09:00 local
        charts.Heatmap[0][0].ShouldBe(50);  // Monday 00:00 local
        charts.DailyByDevice["Laptop"][2].ShouldBe(0.75);
        charts.Devices.Select(d => d.Key).ShouldBe(["device-1", "device-2"]);
    }

    [Fact]
    public void Build_DevicesWithTheSameName_GetDistinctLabels()
    {
        var rows = new List<UsageDto> { Row(Utc(16, 8), 30, deviceId: 1), Row(Utc(16, 9), 10, deviceId: 7) };

        var (_, charts) = WeeklyReportBuilder.Build(rows, s_Monday, s_Vienna, new DateOnly(2026, 11, 22));

        charts.DailyByDevice.Keys.ShouldBe(["Phone (#1)", "Phone (#7)"], ignoreOrder: true);
        charts.Trend.Devices.Count.ShouldBe(2);
    }

    [Fact]
    public void Build_EmptyWeek_HasNoBusiestDay()
    {
        var (report, _) = WeeklyReportBuilder.Build([], s_Monday, s_Vienna, new DateOnly(2026, 11, 22));

        report.Total.ShouldBe(TimeSpan.Zero);
        report.BusiestDay.ShouldBeNull();
        report.Apps.ShouldBeEmpty();
    }
}
