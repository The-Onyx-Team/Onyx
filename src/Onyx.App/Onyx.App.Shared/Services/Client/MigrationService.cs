using Microsoft.Extensions.Logging;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Client;

/// <summary>
/// Moves the plain-text data from before end-to-end encryption into encrypted rows, then has the server delete
/// the plain text. Runs once per account, on the first device that unlocks after the update.
/// </summary>
public sealed class MigrationService(OnyxApiClient api, VaultService vault, AppCatalog catalog,
    ILogger<MigrationService> logger)
{
    private static readonly TimeSpan s_Chunk = TimeSpan.FromDays(30);

    /// <returns>Null when the server has no plain-text data of the user (or can't tell).</returns>
    public async Task<LegacyStatusDto?> GetPendingAsync()
    {
        try
        {
            var status = await api.GetLegacyStatusAsync();
            return status.UsageRows > 0 || status.Apps > 0 || status.Exclusions > 0 ? status : null;
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException)
        {
            logger.LogWarning("Could not check for data to encrypt: {Message}", e.Message);
            return null;
        }
    }

    public async Task MigrateAsync(LegacyStatusDto status, Action<string> progress)
    {
        var v = await vault.GetVaultAsync();

        progress("Encrypting app names and icons…");
        var apps = await api.GetAppsAsync();
        var identifiers = apps.ToDictionary(a => a.Id, a => a.Identifier ?? $"legacy:{a.Name}");
        var infos = new List<EncryptedAppDto>();
        foreach (var app in apps)
            infos.Add(await v.EncryptAppAsync(new AppInfoPayload(identifiers[app.Id], app.ReportedName ?? app.Name,
                app.ReportedCategory ?? app.Category, app.IconBitmap is { Length: > 0 } icon ? icon : null)));
        if (infos.Count > 0)
            await api.PutEncryptedAppsAsync(infos, createOnly: true);

        var exclusions = status.Exclusions > 0 ? await api.GetWebsiteExclusionsAsync() : [];
        await vault.UpdateSettingsAsync(settings =>
        {
            foreach (var app in apps.Where(a => a.IsCustomized))
            {
                var own = new AppSettings
                {
                    Name = app.Name != app.ReportedName ? app.Name : null,
                    Category = app.Category != app.ReportedCategory ? app.Category : null
                };
                if (!own.IsEmpty)
                    settings.Apps.TryAdd(identifiers[app.Id], own);
            }

            settings.ExcludedDomains = settings.ExcludedDomains.Union(exclusions).Order().ToList();
        });

        if (status is { UsageRows: > 0, First: { } first, Last: { } last })
        {
            var moved = 0;
            for (var from = first; from <= last; from += s_Chunk)
            {
                var to = from + s_Chunk;
                var rows = await api.GetUsageAsync(from, to, null);
                if (rows.Count == 0)
                    continue;

                // Hours an updated device already uploaded encrypted are newer than their plain-text copy.
                var existing = (await api.GetEncryptedUsageAsync(from, to))
                    .Select(r => (r.DeviceId, r.Tag, DateTime.SpecifyKind(r.Hour, DateTimeKind.Utc)))
                    .ToHashSet();

                foreach (var device in rows.GroupBy(r => r.DeviceId))
                {
                    var entries = device.Select(r => new UsageEntryDto(
                        identifiers.GetValueOrDefault(r.AppId, $"legacy:{r.AppName}"), r.AppName, r.Category,
                        DateTime.SpecifyKind(r.Date, DateTimeKind.Utc), r.TimeSpan, r.Domain)).ToList();
                    var (encrypted, _) = await v.EncryptUsageAsync(device.Key, entries);
                    var missing = encrypted.Where(e => !existing.Contains((device.Key, e.Tag, e.Hour))).ToList();
                    if (missing.Count > 0)
                        await api.PutEncryptedUsageAsync(device.Key, missing);
                    moved += device.Count();
                }

                progress($"Encrypted {moved:N0} of {status.UsageRows:N0} records…");
            }
        }

        progress("Deleting the unencrypted copy on the server…");
        await api.DeleteLegacyDataAsync();
        catalog.Invalidate();
        logger.LogInformation("Moved the plain-text data into encrypted storage");
    }
}
