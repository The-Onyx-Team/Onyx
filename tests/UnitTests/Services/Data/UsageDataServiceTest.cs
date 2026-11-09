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

[TestSubject(typeof(UsageDataService))]
public sealed class UsageDataServiceTest : IDisposable
{
    private static readonly DateTime s_Now = new(2026, 11, 9, 14, 25, 0, DateTimeKind.Utc);
    private static readonly DateTime s_Hour = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection m_Connection;
    private readonly ApplicationDbContext m_Db;
    private readonly int m_DeviceId;
    private readonly int m_OtherUsersDeviceId;

    public UsageDataServiceTest()
    {
        m_Connection = new SqliteConnection("Data Source=:memory:");
        m_Connection.Open();
        m_Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(m_Connection).Options);
        m_Db.Database.EnsureCreated();

        m_Db.Users.AddRange(
            new ApplicationUser { Id = "user-1", UserName = "one" },
            new ApplicationUser { Id = "user-2", UserName = "two" });
        var device = new Device { Name = "Phone", UserId = "user-1" };
        var otherDevice = new Device { Name = "Other phone", UserId = "user-2" };
        m_Db.Devices.AddRange(device, otherDevice);
        m_Db.SaveChanges();

        m_DeviceId = device.Id;
        m_OtherUsersDeviceId = otherDevice.Id;
    }

    public void Dispose()
    {
        m_Db.Dispose();
        m_Connection.Dispose();
    }

    private UsageDataService CreateService(string userId = "user-1")
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
            }
        };
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(s_Now));

        return new UsageDataService(m_Db, new CurrentUser(accessor, new ServiceCollection().BuildServiceProvider()),
            time.Object);
    }

    private static UsageEntryDto Entry(string app, DateTime hour, int minutes, string? category = null) =>
        new($"android:{app}", app, category, hour, TimeSpan.FromMinutes(minutes));

    [Fact]
    public async Task Upload_SameBucketTwice_ReplacesInsteadOfAdding()
    {
        var service = CreateService();

        await service.UploadUsageAsync(m_DeviceId, [Entry("chat", s_Hour, 10)]);
        await service.UploadUsageAsync(m_DeviceId, [Entry("chat", s_Hour, 25)]);

        var usage = await m_Db.Usages.AsNoTracking().SingleAsync();
        usage.Duration.ShouldBe(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public async Task Upload_CreatesAppsAndCategoriesOnce_AndReportsMissingIcons()
    {
        var service = CreateService();

        var result = await service.UploadUsageAsync(m_DeviceId,
        [
            Entry("chat", s_Hour, 10, "Social"),
            Entry("chat", s_Hour.AddHours(-1), 5, "Social"),
            Entry("maps", s_Hour, 3, "Maps")
        ]);

        result.Stored.ShouldBe(3);
        result.AppsMissingIcon.ShouldBe(["android:chat", "android:maps"], ignoreOrder: true);
        (await m_Db.RegisteredApps.CountAsync()).ShouldBe(2);
        (await m_Db.Categories.CountAsync()).ShouldBe(2);
        (await m_Db.Devices.FindAsync(m_DeviceId))!.LastSeen.ShouldBe(s_Now);
    }

    [Fact]
    public async Task Upload_ToAnotherUsersDevice_Throws()
    {
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            CreateService().UploadUsageAsync(m_OtherUsersDeviceId, [Entry("chat", s_Hour, 10)]));
    }

    [Theory]
    [InlineData(30, 10)] // bucket not on the hour
    [InlineData(0, 0)] // no foreground time
    [InlineData(0, 61)] // more than the bucket can hold
    public async Task Upload_InvalidEntry_Throws(int bucketMinuteOffset, int minutes)
    {
        var entry = Entry("chat", s_Hour.AddMinutes(bucketMinuteOffset), minutes);

        await Should.ThrowAsync<ArgumentException>(() => CreateService().UploadUsageAsync(m_DeviceId, [entry]));
        (await m_Db.Usages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Upload_FutureBucket_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            CreateService().UploadUsageAsync(m_DeviceId, [Entry("chat", s_Hour.AddHours(3), 10)]));
    }

    [Fact]
    public async Task GetUsage_ReturnsOnlyOwnBucketsInRange_AsUtc()
    {
        await CreateService().UploadUsageAsync(m_DeviceId,
            [Entry("chat", s_Hour, 10, "Social"), Entry("chat", s_Hour.AddDays(-2), 20)]);
        await CreateService("user-2").UploadUsageAsync(m_OtherUsersDeviceId, [Entry("chat", s_Hour, 30)]);

        var usage = await CreateService().GetUsageDataAsync(s_Hour.AddHours(-1), s_Hour.AddHours(1));

        var row = usage.ShouldHaveSingleItem();
        row.AppName.ShouldBe("chat");
        row.Category.ShouldBe("Social");
        row.DeviceName.ShouldBe("Phone");
        row.TimeSpan.ShouldBe(TimeSpan.FromMinutes(10));
        row.Date.ShouldBe(s_Hour);
        row.Date.Kind.ShouldBe(DateTimeKind.Utc);
    }
}
