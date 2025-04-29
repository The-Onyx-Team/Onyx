using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;

namespace Onyx.App.Web.Services.Data;

public class UsageDataService(ApplicationDbContext dbContext,  IHttpContextAccessor contextAccessor) 
    : IUsageDataService
{
    public async Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime)
    {
        var user = contextAccessor.HttpContext?.User.FindFirst("sub");

        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return await dbContext.Usages
            .Include(u => u.App)
            .Include(u => u.Device)
            .Include(u => u.Device.User)
            .Where(u => u.Device.UserId == user.Value)
            .Select(u => new UsageDto()
            {
                Action = u.App.Name,
                DeviceName = u.Device.Name,
                DeviceId = u.Device.Id,
                UserId = u.Device.UserId,
                UserName = u.Device.User.UserName,
                Date = u.Date,
                TimeSpan = u.Duration,
                IconUrl = null
            })
            .ToListAsync();
    }

    public Task<List<UsageDto>> GetUsageDataForDeviceAsync(DateTime startTime, DateTime endTime, int deviceId)
    {
        throw new NotImplementedException();
    }

    public Task<List<UsageDto>> GetUsageDataForUserAsync(DateTime startTime, DateTime endTime, string userId)
    {
        throw new NotImplementedException();
    }

    public Task<bool> UploadUsageData(List<UsageDto> usageData)
    {
        throw new NotImplementedException();
    }
}
