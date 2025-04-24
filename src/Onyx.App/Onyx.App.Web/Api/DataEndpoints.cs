using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

public class DataEndpoints
{
    
    public async Task<bool> NewUsage(ApplicationDbContext context,string userid,string date, string fromTime, string toTime, string device, string app )
    {
        try
        {
            DbSet<Usage> usageTable = context.Set<Usage>();
            var newitem = new Usage();
            int devIdhelper;
            int appIdhelper;
            if (usageTable.Any( a => a.Devices.Name == device))
            {
                devIdhelper = usageTable.First( a => a.Devices.Name == device).Devices.Id;
            }
            else
            {
                DbSet<Device> deviceTable = context.Set<Device>();
                await deviceTable.AddAsync(new Device() {Name = device,UserId = userid});
                await context.SaveChangesAsync();
                devIdhelper = usageTable.First( a => a.Devices.Name == device).Devices!.Id;
            }
            if (usageTable.Any(a => a.App.Name == device))
            {
                appIdhelper = usageTable.First( a => a.App.Name == device).App.Id;
            }
            else
            {
                DbSet<RegisteredApp> appTable = context.Set<RegisteredApp>();
                await appTable.AddAsync(new RegisteredApp() {Name = app});
                await context.SaveChangesAsync();
                appIdhelper = usageTable.First( a => a.App.Name == device).App.Id;
            }
            newitem.Date = DateOnly.Parse(date);
            newitem.Duration = new TimeSpan( (int.Parse(fromTime) - int.Parse(toTime)));
            newitem.DeviceId = devIdhelper;
            newitem.AppId = appIdhelper;
            await context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
       
        return true;
    }
}