using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Integrations;

/// <summary>
/// What an integration reads, after decrypting: daily numbers in the user's time zone, limited to what the user
/// allowed (<see cref="IntegrationSettings"/>). Serialized as camelCase JSON; the format is documented in
/// docs/Writerside/topics/Integrations.md, so change it only together with <see cref="Format"/>.
/// </summary>
/// <param name="Format">Version of this layout.</param>
/// <param name="From">First day, included.</param>
/// <param name="To">Last day (today), included; its numbers are as of <paramref name="GeneratedAt"/>.</param>
public sealed record IntegrationSnapshot(
    int Format,
    DateTimeOffset GeneratedAt,
    string TimeZone,
    DateOnly From,
    DateOnly To,
    List<IntegrationDay> Days)
{
    public const int CurrentFormat = 1;
}

/// <param name="AppOpens">How often an app was brought to the front (devices that count it).</param>
/// <param name="Unlocks">Device unlocks (Android).</param>
/// <param name="Apps">Null unless the integration may read apps; likewise for the other lists.</param>
public sealed record IntegrationDay(
    DateOnly Date,
    double ScreenTimeMinutes,
    int AppOpens,
    int Unlocks,
    List<IntegrationApp>? Apps,
    List<IntegrationNamed>? Categories,
    List<IntegrationWebsite>? Websites,
    List<IntegrationNamed>? Devices);

public sealed record IntegrationApp(string Name, string? Category, double Minutes, int Opens);

public sealed record IntegrationWebsite(string Domain, double Minutes, int Visits);

public sealed record IntegrationNamed(string Name, double Minutes);

public static class IntegrationSnapshotBuilder
{
    /// <summary>Days covered for <paramref name="settings"/>, today included.</summary>
    public static (DateOnly From, DateOnly To) Range(IntegrationSettings settings, DateOnly today) =>
        (today.AddDays(-(Math.Clamp(settings.Days, 1, IntegrationSettings.MaxDays) - 1)), today);

    /// <param name="usage">Decrypted usage covering the range (more is ignored).</param>
    public static IntegrationSnapshot Build(IntegrationSettings settings, IReadOnlyList<UsageDto> usage,
        IReadOnlyList<DeviceUnlocks> unlocks, TimeZoneInfo zone, DateOnly today, DateTimeOffset now)
    {
        var (from, to) = Range(settings, today);
        DateOnly DayOf(DateTime utc) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

        var rowsByDay = usage
            .Select(u => (Usage: u, Day: DayOf(u.Date)))
            .Where(r => r.Day >= from && r.Day <= to)
            .ToLookup(r => r.Day, r => r.Usage);
        var unlocksByDay = unlocks
            .GroupBy(u => DayOf(u.Hour))
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Count));

        var days = new List<IntegrationDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var rows = rowsByDay[day].ToList();
            days.Add(new IntegrationDay(
                day,
                Minutes(rows),
                rows.Where(r => r.Domain is null).Sum(r => r.Opens),
                unlocksByDay.GetValueOrDefault(day),
                settings.Apps
                    ? rows.GroupBy(r => r.AppId)
                        .Select(g => new IntegrationApp(g.First().AppName, g.First().Category, Minutes(g),
                            g.Where(r => r.Domain is null).Sum(r => r.Opens)))
                        .OrderByDescending(a => a.Minutes).ToList()
                    : null,
                settings.Categories
                    ? rows.GroupBy(r => r.Category ?? "Uncategorized")
                        .Select(g => new IntegrationNamed(g.Key, Minutes(g)))
                        .OrderByDescending(c => c.Minutes).ToList()
                    : null,
                settings.Websites
                    ? rows.Where(r => r.Domain is not null).GroupBy(r => r.Domain!)
                        .Select(g => new IntegrationWebsite(g.Key, Minutes(g), g.Sum(r => r.Opens)))
                        .OrderByDescending(w => w.Minutes).ToList()
                    : null,
                settings.Devices
                    ? rows.GroupBy(r => r.DeviceName)
                        .Select(g => new IntegrationNamed(g.Key, Minutes(g)))
                        .OrderByDescending(d => d.Minutes).ToList()
                    : null));
        }

        return new IntegrationSnapshot(IntegrationSnapshot.CurrentFormat, now, zone.Id, from, to, days);
    }

    private static double Minutes(IEnumerable<UsageDto> rows) =>
        Math.Round(rows.Sum(r => r.TimeSpan.TotalMinutes), 1);
}
