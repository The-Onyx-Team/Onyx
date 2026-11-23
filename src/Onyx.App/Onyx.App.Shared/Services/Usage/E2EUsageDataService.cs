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

    public async Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime, int? deviceId = null)
    {
        var rows = await GetDecryptedAsync(startTime, endTime, deviceId);
        var groups = await catalog.GroupsByTagAsync();
        var settings = await vault.GetSettingsAsync();
        var excluded = settings.ExcludedDomains;
        m_Devices ??= await devices.GetDevices();
        var deviceNames = m_Devices.ToDictionary(d => d.Id, d => d.Name);

        var result = new List<UsageDto>(rows.Count);
        foreach (var row in rows)
        {
            var group = groups.GetValueOrDefault(row.Tag);
            if (group?.Hidden == true)
                continue;
            var entry = group?.Members.First(m => m.Tag == row.Tag);
            foreach (var part in row.Payload.Parts)
            {
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
                    Domain = part.Domain is { } d && !WebDomain.IsExcluded(d, excluded) ? d : null
                });
            }
        }

        return result;
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
