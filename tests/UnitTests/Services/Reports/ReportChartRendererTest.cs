using System.Diagnostics;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Onyx.App.Web.Services.Reports;
using Onyx.Data.ApiSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Reports;

[TestSubject(typeof(ReportChartRenderer))]
public class ReportChartRendererTest
{
    private static ReportChartRenderer Renderer(string? python = null) => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(python is null ? [] : [new("Onyx:Reports:Python", python)])
            .Build(),
        NullLogger<ReportChartRenderer>.Instance);

    private static bool MatplotlibInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("python3", "-c \"import matplotlib\"")
                { RedirectStandardError = true, RedirectStandardOutput = true });
            process!.WaitForExit(30_000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task MissingPython_ReturnsAMessageInsteadOfFailing()
    {
        var (charts, error) = await Renderer("/nonexistent/python3").RenderAsync(new ChartInput());

        charts.ShouldBeEmpty();
        error.ShouldNotBeNull();
        error.ShouldContain("Python");
    }

    /// <summary>Runs the real script. Skipped (passes) where Python with matplotlib is not installed.</summary>
    [Fact]
    public async Task Script_RendersEveryChartAsSvg()
    {
        if (!MatplotlibInstalled())
            return;

        var monday = new DateOnly(2026, 11, 16);
        var rows = Enumerable.Range(0, 7 * 24 * 2)
            .Select(h => new UsageDto
            {
                AppId = h % 3, AppName = $"App <{h % 3}> & co", Category = h % 2 == 0 ? "Social" : null,
                Date = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc).AddHours(h),
                TimeSpan = TimeSpan.FromMinutes(h % 50), DeviceId = 1 + h % 2, DeviceName = h % 2 == 0 ? "Phone" : "Laptop"
            })
            .ToList();
        var (_, input) = WeeklyReportBuilder.Build(rows, monday, TimeZoneInfo.Utc, new DateOnly(2026, 11, 22));

        var (charts, error) = await Renderer().RenderAsync(input);

        error.ShouldBeNull();
        charts.Keys.ShouldBe(["daily", "apps", "categories", "trend", "heatmap", "device-1", "device-2"], ignoreOrder: true);
        charts.Values.ShouldAllBe(svg => svg.Contains("<svg"));
    }
}
