using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Services.Data;

public class UsageDataService(ApplicationDbContext dbContext, IHttpContextAccessor contextAccessor)
    : IUsageDataService
{
    private const string SubClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    
    public async Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime)
    {
        var user = contextAccessor.HttpContext?.User.FindFirst(SubClaim);

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return await dbContext.Usages
            .Include(u => u.App)
            .Include(u => u.Device)
            .Include(u => u.Device!.User)
            .Where(u => u.Device!.UserId == user.Value)
            .Where(u => u.Date >= startTime && u.Date <= endTime)
            .Select(u => new UsageDto()
            {
                Action = u.App!.Name!,
                DeviceName = u.Device!.Name!,
                DeviceId = u.Device.Id,
                UserId = u.Device.UserId!,
                UserName = u.Device.User!.UserName!,
                Date = u.Date,
                TimeSpan = u.Duration,
                IconUrl = null
            })
            .ToListAsync();
    }

    public async Task<List<UsageDto>> GetUsageDataForDeviceAsync(DateTime startTime, DateTime endTime, int deviceId)
    {
        var user = contextAccessor.HttpContext?.User.FindFirst(SubClaim);

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return await dbContext.Usages
            .Include(u => u.App)
            .Include(u => u.Device)
            .Include(u => u.Device!.User)
            .Where(u => u.Device!.UserId == user.Value)
            .Where(u => u.Device!.Id == deviceId)
            .Where(u => u.Date >= startTime && u.Date <= endTime)
            .Select(u => new UsageDto()
            {
                Action = u.App!.Name!,
                DeviceName = u.Device!.Name!,
                DeviceId = u.Device.Id,
                UserId = u.Device.UserId!,
                UserName = u.Device.User!.UserName!,
                Date = u.Date,
                TimeSpan = u.Duration,
                IconUrl = null
            })
            .ToListAsync();
    }

    public async Task<bool> UploadUsageData(List<UsageDto> usageData, int appId)
    {
        var user = contextAccessor.HttpContext?.User.FindFirst(SubClaim);

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        var device = dbContext.Devices
            .FirstOrDefault(d => d.UserId == user.Value && d.Name == usageData.First().DeviceName);

        if (device is null)
        {
            throw new KeyNotFoundException("Device not found");
        }

        foreach (var newUsage in usageData.Select(usage => new Usage
                 {
                     Id = Guid.NewGuid().ToString(),
                     Date = usage.Date,
                     Duration = usage.TimeSpan,
                     DeviceId = device.Id,
                     AppId = appId
                 }))
        {
            dbContext.Usages.Add(newUsage);
        }

        await dbContext.SaveChangesAsync();

        return true;
    }
}