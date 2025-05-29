namespace Onyx.App.Web.Services.Data;

public class AppManager(ApplicationDbContext dbContext) : IAppManager
{
    public async Task<int> RegisterAppAsync(string name, byte[] iconBitmap)
    {
        var app = new RegisteredApp()
        {
            Name = name,
            IconBitmap = iconBitmap
        };

        dbContext.RegisteredApps.Add(app);
        await dbContext.SaveChangesAsync();

        app = await dbContext.RegisteredApps
            .Where(a => a.Name == name)
            .FirstOrDefaultAsync();

        if (app is null)
            throw new Exception("App not found after registration");

        return app.Id;
    }

    public Task<List<AppDto>> GetAppsAsync() =>
        dbContext.RegisteredApps
            .Select(a => new AppDto()
            {
                Id = a.Id,
                Name = a.Name!,
                IconBitmap = a.IconBitmap ?? Array.Empty<byte>()
            })
            .ToListAsync();
}