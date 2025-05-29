namespace Onyx.App.Web.Services.Database;

public class DbInitializer(
    IWebHostEnvironment env,
    IServiceProvider serviceProvider,
    ILogger<DbInitializer> logger
) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";

    private readonly ActivitySource m_ActivitySource = new(ActivitySourceName);
    private ApplicationDbContext m_DbContext = null!;
    private UserManager<ApplicationUser> m_UserManager = null!;
    private RoleManager<ApplicationRole> m_RoleManager = null!;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        m_DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        m_UserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        m_RoleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        await InitializeDatabaseAsync(cancellationToken);
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        using var activity = m_ActivitySource.StartActivity(ActivityKind.Client);

        var sw = Stopwatch.StartNew();

        var strategy = m_DbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(m_DbContext.Database.MigrateAsync, cancellationToken);

        await SeedAsync(cancellationToken);

        logger.LogInformation("System Database initialization completed after {ElapsedMilliseconds}ms",
            sw.ElapsedMilliseconds);

        logger.LogInformation("Plugin Database System initialization completed after {ElapsedMilliseconds}ms",
            sw.ElapsedMilliseconds);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding database");

        if (env.IsDevelopment())
        {
            if (await m_UserManager.FindByNameAsync("admin") is not null)
                return;

            await m_UserManager.CreateAsync(new ApplicationUser
                {
                    UserName = "admin",
                    Email = "admin@admin.com"
                },
                "Admin123456");

            var user = await m_UserManager.FindByNameAsync("admin");

            m_DbContext.Devices.Add(new Device()
            {
                Name = "Admin Device 1",
                UserId = user!.Id,
            });
            m_DbContext.Devices.Add(new Device()
            {
                Name = "Admin Device 2",
                UserId = user!.Id,
            });
            m_DbContext.Devices.Add(new Device()
            {
                Name = "Admin Device 3",
                UserId = user!.Id,
            });

            m_DbContext.RegisteredApps.Add(new RegisteredApp()
            {
                Name = "Test App 1"
            });
            m_DbContext.RegisteredApps.Add(new RegisteredApp()
            {
                Name = "Test App 2"
            });
            m_DbContext.RegisteredApps.Add(new RegisteredApp()
            {
                Name = "Test App 3"
            });

            m_DbContext.Categories.AddRange([
                new Category()
                {
                    Name = "Test Category 1"
                },
                new Category()
                {
                    Name = "Test Category 2"
                },
                new Category()
                {
                    Name = "Test Category 3"
                }
            ]);

            m_DbContext.Usages.AddRange([
                new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now.AddDays(-1),
                    Duration = TimeSpan.FromHours(1),
                    DeviceId = 1,
                    AppId = 1,
                    CategoryId = 1,
                },
                new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now.AddDays(-2),
                    Duration = TimeSpan.FromHours(2),
                    DeviceId = 1,
                    AppId = 2,
                    CategoryId = 2,
                },
                new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now.AddDays(-3),
                    Duration = TimeSpan.FromHours(3),
                    DeviceId = 1,
                    AppId = 3,
                    CategoryId = 3,
                },
                new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now.AddDays(-1),
                    Duration = TimeSpan.FromHours(4),
                    DeviceId = 2,
                    AppId = 1,
                    CategoryId = 1,
                },
                new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now.AddDays(-2),
                    Duration = TimeSpan.FromHours(5),
                    DeviceId = 2,
                    AppId = 2,
                    CategoryId = 2,
                },
                new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = DateTime.Now.AddDays(-3),
                    Duration = TimeSpan.FromHours(6),
                    DeviceId = 2,
                    AppId = 3,
                    CategoryId = 3,
                },
            ]);
            
            await m_DbContext.SaveChangesAsync(cancellationToken);
        }

        await m_DbContext.SaveChangesAsync(cancellationToken);
    }
}