using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Onyx.App.Web.Services.Auth;
using Onyx.App.Web.Services.Data;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Data;

[TestSubject(typeof(AppManager))]
public sealed class AppOverrideTest : IDisposable
{
    private static readonly DateTime s_Hour = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection m_Connection;
    private readonly ApplicationDbContext m_Db;
    private readonly int m_AppId;

    public AppOverrideTest()
    {
        m_Connection = new SqliteConnection("Data Source=:memory:");
        m_Connection.Open();
        m_Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(m_Connection).Options);
        m_Db.Database.EnsureCreated();

        m_Db.Users.AddRange(new ApplicationUser { Id = "user-1", UserName = "one" },
            new ApplicationUser { Id = "user-2", UserName = "two" });
        var device1 = new Device { Name = "Phone", UserId = "user-1" };
        var device2 = new Device { Name = "Other phone", UserId = "user-2" };
        m_Db.Devices.AddRange(device1, device2);
        m_Db.SaveChanges();

        // Both users upload usage of the same (shared) app.
        var entry = new UsageEntryDto("android:com.example.x", "com.example.x", null, s_Hour, TimeSpan.FromMinutes(20));
        Usage("user-1").UploadUsageAsync(device1.Id, [entry]).GetAwaiter().GetResult();
        Usage("user-2").UploadUsageAsync(device2.Id, [entry]).GetAwaiter().GetResult();
        m_AppId = m_Db.RegisteredApps.Single().Id;
    }

    public void Dispose()
    {
        m_Db.Dispose();
        m_Connection.Dispose();
    }

    private CurrentUser User(string userId) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
        }
    });

    private AppManager Apps(string userId = "user-1") => new(m_Db, User(userId));

    private UsageDataService Usage(string userId = "user-1")
    {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(s_Hour.AddMinutes(30)));
        return new UsageDataService(m_Db, User(userId), time.Object);
    }

    [Fact]
    public async Task Override_ChangesNameAndCategoryForThatUserOnly()
    {
        (await Apps().SetOverrideAsync(m_AppId, " Example ", "Tools")).ShouldBeTrue();

        var mine = (await Apps().GetAppsAsync()).Single();
        mine.Name.ShouldBe("Example");
        mine.Category.ShouldBe("Tools");
        mine.ReportedName.ShouldBe("com.example.x");
        mine.IsCustomized.ShouldBeTrue();

        var usage = await Usage().GetUsageDataAsync(s_Hour, s_Hour.AddHours(1));
        usage.Single().AppName.ShouldBe("Example");
        usage.Single().Category.ShouldBe("Tools");

        // Apps are shared: the other user still sees the reported name.
        (await Apps("user-2").GetAppsAsync()).Single().Name.ShouldBe("com.example.x");
        (await Usage("user-2").GetUsageDataAsync(s_Hour, s_Hour.AddHours(1))).Single().AppName.ShouldBe("com.example.x");
    }

    [Fact]
    public async Task Override_OnlyCategory_KeepsReportedName()
    {
        await Apps().SetOverrideAsync(m_AppId, "  ", "Games");

        var app = (await Apps().GetAppsAsync()).Single();
        app.Name.ShouldBe("com.example.x");
        app.Category.ShouldBe("Games");
    }

    [Fact]
    public async Task Override_BothEmpty_RemovesIt()
    {
        await Apps().SetOverrideAsync(m_AppId, "Example", null);
        await Apps().SetOverrideAsync(m_AppId, "", null);

        m_Db.AppOverrides.Count().ShouldBe(0);
        (await Apps().GetAppsAsync()).Single().IsCustomized.ShouldBeFalse();
    }

    [Fact]
    public async Task Override_AppWithoutOwnUsage_IsRejected()
    {
        var foreign = new RegisteredApp { Identifier = "android:other", Name = "other" };
        m_Db.RegisteredApps.Add(foreign);
        await m_Db.SaveChangesAsync();

        (await Apps().SetOverrideAsync(foreign.Id, "Mine", null)).ShouldBeFalse();
        m_Db.AppOverrides.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Override_TooLongName_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(() => Apps().SetOverrideAsync(m_AppId, new string('x', 201), null));
    }

    [Fact]
    public async Task DeletingTheUser_RemovesTheOverrides()
    {
        await Apps().SetOverrideAsync(m_AppId, "Example", null);

        m_Db.Usages.RemoveRange(m_Db.Usages.Where(u => u.Device!.UserId == "user-1"));
        m_Db.Devices.RemoveRange(m_Db.Devices.Where(d => d.UserId == "user-1"));
        m_Db.Users.Remove(m_Db.Users.Single(u => u.Id == "user-1"));
        await m_Db.SaveChangesAsync();

        m_Db.AppOverrides.Count().ShouldBe(0);
    }
}
