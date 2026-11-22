using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Services.Api;

// API-backed implementations of the services the shared dashboard uses. The web host implements the same
// interfaces against the database.

public class ApiDeviceManager(OnyxApiClient client) : IDeviceManager
{
    public Task<List<DeviceDto>> GetDevices() => client.GetDevicesAsync();

    public Task<DeviceDto> RegisterDeviceAsync(string deviceName, string? platform) =>
        client.RegisterDeviceAsync(deviceName, platform);

    public Task UnregisterDeviceAsync(int deviceId) => client.DeleteDeviceAsync(deviceId);
}

public class ApiUsageDataService(OnyxApiClient client) : IUsageDataService
{
    public Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime, int? deviceId = null) =>
        client.GetUsageAsync(startTime, endTime, deviceId);

    /// <remarks>The app's collectors upload through <see cref="OnyxApiClient.UploadUsageAsync"/>, which also
    /// handles device registration; the UI never uploads.</remarks>
    public Task<UsageUploadResultDto> UploadUsageAsync(int deviceId, IReadOnlyList<UsageEntryDto> entries) =>
        throw new NotSupportedException("Uploads go through the platform collector");
}

public class ApiAppManager(OnyxApiClient client) : IAppManager
{
    public Task<List<AppDto>> GetAppsAsync() => client.GetAppsAsync();

    public async Task<bool> SetIconAsync(string appIdentifier, byte[] iconPng)
    {
        await client.UploadIconAsync(appIdentifier, iconPng);
        return true;
    }

    public async Task<bool> SetOverrideAsync(int appId, string? displayName, string? category)
    {
        try
        {
            await client.SetAppOverrideAsync(appId, new AppOverrideDto(displayName, category));
            return true;
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            throw new ArgumentException(OnyxApiClient.ErrorMessage(e));
        }
    }
}

public class ApiWebsiteSettings(OnyxApiClient client) : IWebsiteSettings
{
    public Task<List<string>> GetExcludedDomainsAsync() => client.GetWebsiteExclusionsAsync();

    public async Task<List<string>> SetExcludedDomainsAsync(IReadOnlyList<string> domains)
    {
        try
        {
            return await client.SetWebsiteExclusionsAsync(domains);
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Same contract as the web implementation: invalid input is an ArgumentException.
            throw new ArgumentException(e.Message);
        }
    }

    public Task DeleteWebsiteHistoryAsync() => client.DeleteWebsiteHistoryAsync();
}
