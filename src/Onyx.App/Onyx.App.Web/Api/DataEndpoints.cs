using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

public class DataEndpoints
{
    
    public async Task<bool> NewUsage(ApplicationDbContext context,string date, string fromTime, string toTime, string device, string app )
    {
        DbSet<Usage> usageTable = context.Set<Usage>();
        DbSet<Device> deviceTable = context.Set<Device>();
        DbSet<RegisteredApp> appTable = context.Set<RegisteredApp>();
        var newitem = new Usage();
        int devIdhelper;
        int appIdhelper;
        if (usageTable.Where( a => a.Devices.Name == device).Any())
        {
            devIdhelper = usageTable.Where( a => a.Devices.Name == device).First().Devices.Id;
        }
        else
        {
            await deviceTable.AddAsync(new Device() {Name = device,UserId = temp});
            await context.SaveChangesAsync();
            devIdhelper = usageTable.Where( a => a.Devices.Name == device).First().Devices.Id;
        }
        if (usageTable.Where( a => a.App.Name == device).Any())
        {
            appIdhelper = usageTable.Where( a => a.App.Name == device).First().App.Id;
        }
        else
        {
            await appTable.AddAsync(new RegisteredApp() {Name = app});
            await context.SaveChangesAsync();
            appIdhelper = usageTable.Where( a => a.App.Name == device).First().App.Id;
        }

        newitem.Date = DateOnly.Parse(date);
        newitem.Duration = new TimeSpan( (int.Parse(fromTime) - int.Parse(toTime)));
        newitem.DeviceId = devIdhelper;
        newitem.AppId = appIdhelper;
        return true;
    }
}