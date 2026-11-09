using JetBrains.Annotations;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.Data.ApiSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

[TestSubject(typeof(UsageSummary))]
public class UsageSummaryTest
{
    // UTC+1 in November.
    private static readonly TimeZoneInfo s_Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private static UsageDto Usage(int appId, string app, DateTime utcHour, int minutes, string device = "Phone",
        string? category = null) => new()
    {
        AppId = appId,
        AppName = app,
        Category = category,
        Date = DateTime.SpecifyKind(utcHour, DateTimeKind.Utc),
        TimeSpan = TimeSpan.FromMinutes(minutes),
        DeviceId = 1,
        DeviceName = device
    };

    [Fact]
    public void Period_WeekStartsOnMonday()
    {
        var (start, end) = UsageSummary.Period(new DateTime(2026, 11, 8), DashboardRange.Week); // a Sunday

        start.ShouldBe(new DateTime(2026, 11, 2));
        end.ShouldBe(new DateTime(2026, 11, 9));
    }

    [Fact]
    public void Create_UsesViewerTimeZoneForDayBoundaries()
    {
        var day = new DateTime(2026, 11, 9);
        var usage = new[]
        {
            Usage(1, "Chat", new DateTime(2026, 11, 8, 23, 0, 0), 30), // 00:00 local on the 9th
            Usage(1, "Chat", new DateTime(2026, 11, 8, 22, 0, 0), 45) // 23:00 local on the 8th: excluded
        };

        var summary = UsageSummary.Create(usage, day, day.AddDays(1), DashboardRange.Day, s_Vienna);

        summary.Total.ShouldBe(TimeSpan.FromMinutes(30));
        summary.Series.ShouldHaveSingleItem().Hours[0].ShouldBe(0.5);
    }

    [Fact]
    public void Create_RanksAppsAndGroupsTheRestAsOther()
    {
        var day = new DateTime(2026, 11, 9);
        var hour = new DateTime(2026, 11, 9, 8, 0, 0);
        var usage = Enumerable.Range(1, UsageSummary.ChartApps + 2)
            .Select(i => Usage(i, $"App {i}", hour, 60 - i, category: i % 2 == 0 ? "Games" : null))
            .ToList();

        var summary = UsageSummary.Create(usage, day, day.AddDays(1), DashboardRange.Day, s_Vienna);

        summary.Apps[0].Name.ShouldBe("App 1");
        summary.Series.Count.ShouldBe(UsageSummary.ChartApps + 1);
        summary.Series[^1].Name.ShouldBe("Other");
        summary.Series.Sum(s => s.Hours.Sum()).ShouldBe(summary.Total.TotalHours, 1e-9);
        summary.Categories.Select(c => c.Name).ShouldBe(["Uncategorized", "Games"], ignoreOrder: true);
        summary.Apps.Sum(a => a.Share).ShouldBe(1, 1e-9);
    }

    [Fact]
    public void Create_WeekHasOneColumnPerDayAndSplitsDevices()
    {
        var (start, end) = UsageSummary.Period(new DateTime(2026, 11, 9), DashboardRange.Week);
        var usage = new[]
        {
            Usage(1, "Chat", new DateTime(2026, 11, 9, 10, 0, 0), 30, "Phone"),
            Usage(1, "Chat", new DateTime(2026, 11, 11, 10, 0, 0), 30, "Laptop")
        };

        var summary = UsageSummary.Create(usage, start, end, DashboardRange.Week, s_Vienna);

        summary.PeriodLabels.Length.ShouldBe(7);
        summary.Series[0].Hours[0].ShouldBe(0.5);
        summary.Series[0].Hours[2].ShouldBe(0.5);
        summary.Devices.Select(d => d.Share).ShouldAllBe(s => Math.Abs(s - 0.5) < 1e-9);
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(0.2, "1 min")]
    [InlineData(42, "42 min")]
    [InlineData(135, "2 h 15 min")]
    public void Format(double minutes, string expected) =>
        UsageSummary.Format(TimeSpan.FromMinutes(minutes)).ShouldBe(expected);
}
