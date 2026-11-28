using Onyx.App.Shared.Services.Client;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>Loads encrypted usage from the server and decrypts it here.</summary>
public sealed class E2EUsageDataService(OnyxApiClient api, VaultService vault, AppCatalog catalog, IDeviceManager devices)
    : IUsageDataService
{
    private List<DeviceDto>? m_Devices;

    public async Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime, int? deviceId = null) =>
        (await GetUsageAndUnlocksAsync(startTime, endTime, deviceId)).Usage;

    public async Task<(List<UsageDto> Usage, List<DeviceUnlocks> Unlocks)> GetUsageAndUnlocksAsync(DateTime startTime,
        DateTime endTime, int? deviceId = null)
    {
        var rows = await GetDecryptedAsync(startTime, endTime, deviceId);
        var groups = await catalog.GroupsByTagAsync();
        var settings = await vault.GetSettingsAsync();
        var excluded = settings.ExcludedDomains;
        m_Devices ??= await devices.GetDevices();
        var deviceNames = m_Devices.ToDictionary(d => d.Id, d => d.Name);

        var result = new List<UsageDto>(rows.Count);
        var unlocks = new List<DeviceUnlocks>();
        foreach (var row in rows)
        {
            if (row.Payload.AppIdentifier == DeviceActivity.Identifier)
            {
                var count = row.Payload.Parts.Sum(p => p.Opens);
                if (count > 0)
                    unlocks.Add(new DeviceUnlocks(row.DeviceId,
                        deviceNames.GetValueOrDefault(row.DeviceId, "Removed device"), row.Hour, count));
                continue;
            }

            var group = groups.GetValueOrDefault(row.Tag);
            if (group?.Hidden == true)
                continue;
            var entry = group?.Members.First(m => m.Tag == row.Tag);
            foreach (var part in row.Payload.Parts)
            {
                var domain = part.Domain is { } d && !WebDomain.IsExcluded(d, excluded) ? d : null;
                result.Add(new UsageDto
                {
                    // Apps without information yet (the device hasn't sent it) are shown by their identifier.
                    AppId = group?.Id ?? -Math.Abs(row.Tag.GetHashCode()),
                    AppName = group?.Name ?? row.Payload.AppIdentifier,
                    Category = group?.Category ?? entry?.Category,
                    DeviceId = row.DeviceId,
                    DeviceName = deviceNames.GetValueOrDefault(row.DeviceId, "Removed device"),
                    Date = row.Hour,
                    TimeSpan = TimeSpan.FromSeconds(part.Seconds),
                    Domain = domain,
                    // Visits of an excluded website are not opens of the browser.
                    Opens = domain is null && part.Domain is not null ? 0 : part.Opens
                });
            }
        }

        return (result, unlocks);
    }

    /// <summary>The decrypted rows, as stored (one per device, app and hour).</summary>
    public async Task<List<DecryptedUsage>> GetDecryptedAsync(DateTime startTime, DateTime endTime, int? deviceId = null)
    {
        var v = await vault.GetVaultAsync();
        var start = startTime.ToUniversalTime();
        var end = endTime.ToUniversalTime();
        var result = new List<DecryptedUsage>();
        // The server answers at most a year per request.
        for (var from = start; from < end; from = from.AddDays(365))
        {
            var to = from.AddDays(365) < end ? from.AddDays(365) : end;
            result.AddRange(await v.DecryptUsageAsync(await api.GetEncryptedUsageAsync(from, to, deviceId)));
        }

        return result;
    }

    public void InvalidateDevices() => m_Devices = null;
}
