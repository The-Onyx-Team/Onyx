using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Services.Data;

public class DeviceManager(ApplicationDbContext dbContext, CurrentUser currentUser) : IDeviceManager
{
    public async Task<List<DeviceDto>> GetDevices()
    {
        var userId = await currentUser.GetRequiredUserIdAsync();

        var devices = await dbContext.Devices
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.Name)
            .Select(d => new DeviceDto
            {
                Id = d.Id,
                Name = d.Name!,
                Platform = d.Platform,
                LastSeen = d.LastSeen
            })
            .ToListAsync();

        // Stored as UTC; SQLite returns Kind=Unspecified.
        foreach (var device in devices.Where(d => d.LastSeen is not null))
            device.LastSeen = DateTime.SpecifyKind(device.LastSeen!.Value, DateTimeKind.Utc);

        return devices;
    }

    public async Task<DeviceDto> RegisterDeviceAsync(string deviceName, string? platform)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            throw new ArgumentException("Device name cannot be null or empty", nameof(deviceName));

        var userId = await currentUser.GetRequiredUserIdAsync();

        var device = new Device
        {
            Name = Truncate(deviceName.Trim(), 500),
            Platform = string.IsNullOrWhiteSpace(platform) ? null : Truncate(platform.Trim(), 50),
            UserId = userId
        };

        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();

        return new DeviceDto { Id = device.Id, Name = device.Name, Platform = device.Platform };
    }

    public async Task UnregisterDeviceAsync(int deviceId)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();

        var device = await dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId)
                     ?? throw new KeyNotFoundException("Device not found");

        // Usage rows reference the device with a cascading foreign key.
        dbContext.Devices.Remove(device);
        await dbContext.SaveChangesAsync();
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
