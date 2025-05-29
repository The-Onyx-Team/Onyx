namespace Onyx.App.Shared.Services.Usage;

public interface IDeviceManager
{
    public Task<List<DeviceDto>> GetDevices();
    public Task RegisterDeviceAsync(string deviceName);
    public Task UnregisterDeviceAsync(int deviceId);
}