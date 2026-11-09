using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

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

            SeedUsage(user!.Id);
            await m_DbContext.SaveChangesAsync(cancellationToken);
        }

        await m_DbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Two weeks of plausible hourly usage on two devices, so the dashboard has something to show in development.
    /// Deterministic, so screenshots and manual checks are reproducible.
    /// </summary>
    private void SeedUsage(string userId)
    {
        var phone = new Device { Name = "Pixel 9", Platform = "Android", UserId = userId, LastSeen = DateTime.UtcNow };
        var laptop = new Device { Name = "Laptop", Platform = "Linux", UserId = userId, LastSeen = DateTime.UtcNow };
        m_DbContext.Devices.AddRange(phone, laptop);

        var social = new Category { Name = "Social" };
        var video = new Category { Name = "Video" };
        var development = new Category { Name = "Development" };
        var network = new Category { Name = "Network" };
        m_DbContext.Categories.AddRange(social, video, development, network);

        RegisteredApp App(string identifier, string name, Category category)
        {
            var app = new RegisteredApp { Identifier = identifier, Name = name, Category = category };
            m_DbContext.RegisteredApps.Add(app);
            return app;
        }

        (Device Device, RegisteredApp App, int[] Hours, int Minutes)[] habits =
        [
            (phone, App("android:com.whatsapp", "WhatsApp", social), [7, 12, 18, 21], 14),
            (phone, App("android:com.instagram.android", "Instagram", social), [8, 13, 20, 22], 22),
            (phone, App("android:com.google.android.youtube", "YouTube", video), [19, 21, 22], 35),
            (laptop, App("linux:code", "Visual Studio Code", development), [9, 10, 11, 14, 15, 16], 48),
            (laptop, App("linux:firefox", "Firefox", network), [9, 11, 13, 15, 17, 20], 26),
            (laptop, App("linux:spotify", "Spotify", video), [10, 14, 16], 40)
        ];

        var random = new Random(42);
        var today = DateTime.UtcNow.Date;
        for (var day = 13; day >= 0; day--)
        {
            var date = today.AddDays(-day);
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            foreach (var (device, app, hours, minutes) in habits)
            {
                // Less laptop on weekends, more phone.
                var factor = weekend ? (device == laptop ? 0.3 : 1.4) : 1.0;
                foreach (var hour in hours)
                {
                    var bucket = date.AddHours(hour);
                    if (bucket > DateTime.UtcNow)
                        continue;

                    var duration = Math.Clamp(minutes * factor * (0.5 + random.NextDouble()), 1, 60);
                    m_DbContext.Usages.Add(new Usage
                    {
                        Id = Guid.NewGuid().ToString(),
                        Device = device,
                        App = app,
                        Date = bucket,
                        Duration = TimeSpan.FromMinutes(Math.Round(duration))
                    });
                }
            }
        }
    }
}