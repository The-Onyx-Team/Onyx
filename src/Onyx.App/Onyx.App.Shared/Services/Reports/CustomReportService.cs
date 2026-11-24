using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Documents;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core.Crypto;

namespace Onyx.App.Shared.Services.Reports;

/// <summary>The user's own reports: kept in the encrypted settings, built on this device from the decrypted data.</summary>
public sealed class CustomReportService(VaultService vault, IUsageDataService usage, AppCatalog catalog, TimeProvider time)
{
    public async Task<IReadOnlyList<CustomReportSettings>> GetAllAsync() => (await vault.GetSettingsAsync()).CustomReports;

    public Task SaveAsync(CustomReportSettings report) => vault.UpdateSettingsAsync(settings =>
    {
        var index = settings.CustomReports.FindIndex(r => r.Id == report.Id);
        if (index >= 0)
            settings.CustomReports[index] = report;
        else
            settings.CustomReports.Add(report);
    });

    public Task DeleteAsync(string id) => vault.UpdateSettingsAsync(settings => settings.CustomReports.RemoveAll(r => r.Id == id));

    /// <summary>Builds the report for its period as of today in <paramref name="zone"/>.</summary>
    public async Task<ReportLayout> BuildAsync(CustomReportSettings report, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var (from, to) = report.Range(today);
        var (start, end) = CustomReportBuilder.UtcRange(from, to, zone);
        var rows = await usage.GetUsageDataAsync(start, end);

        HashSet<string>? names = null;
        if (report.Apps.Apps.Count > 0)
        {
            names = (await catalog.GetGroupsAsync())
                .Where(g => g.Members.Any(m => report.Apps.Apps.Contains(m.Identifier)))
                .Select(g => g.Name)
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        }

        var colors = (await vault.GetSettingsAsync()).Categories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Color, StringComparer.OrdinalIgnoreCase);
        return CustomReportBuilder.Build(report, rows, from, to, zone, names, colors);
    }

    public byte[] ToPdf(ReportLayout layout) => ReportPdf.Render(layout, time.GetUtcNow().UtcDateTime);
}
