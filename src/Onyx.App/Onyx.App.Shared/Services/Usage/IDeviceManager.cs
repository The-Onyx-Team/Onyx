using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>Devices of the signed-in user.</summary>
public interface IDeviceManager
{
    public Task<List<DeviceDto>> GetDevices();

    /// <returns>The new device, including the id the collector uploads under.</returns>
    public Task<DeviceDto> RegisterDeviceAsync(string deviceName, string? platform);

    /// <summary>Removes the device and all of its usage data.</summary>
    /// <exception cref="KeyNotFoundException">The device does not exist or belongs to another user.</exception>
    public Task UnregisterDeviceAsync(int deviceId);
}
