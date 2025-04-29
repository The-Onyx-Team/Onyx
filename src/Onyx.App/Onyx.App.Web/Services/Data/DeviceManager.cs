using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Services.Data;

public class DeviceManager(ApplicationDbContext dbContext, IHttpContextAccessor contextAccessor)
    : IDeviceManager
{
    private const string SubClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    
    public async Task<List<DeviceDto>> GetDevices()
    {
        var user = contextAccessor.HttpContext?.User.FindFirst(SubClaim);

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return await dbContext.Devices
            .Where(d => d.UserId == user.Value)
            .Select(d => new DeviceDto
            {
                Name = d.Name!
            })
            .ToListAsync();
    }

    public Task RegisterDeviceAsync(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            throw new ArgumentException("Device name cannot be null or empty", nameof(deviceName));
        }

        var user = contextAccessor.HttpContext?.User.FindFirst(SubClaim);

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        var device = new Device
        {
            Name = deviceName,
            UserId = user.Value
        };

        dbContext.Devices.Add(device);
        return dbContext.SaveChangesAsync();
    }

    public Task UnregisterDeviceAsync(int deviceId)
    {
        var user = contextAccessor.HttpContext?.User.FindFirst(SubClaim);

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        var device = dbContext.Devices.Find(deviceId);
        if (device is null || device.UserId != user.Value)
        {
            throw new KeyNotFoundException("Device not found");
        }

        dbContext.Devices.Remove(device);
        return dbContext.SaveChangesAsync();
    }
}