using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Onyx.App.Web.Services.Auth;
using Onyx.App.Web.Services.Data;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Data;

[TestSubject(typeof(WebDomain))]
public class WebDomainTest
{
    [Theory]
    [InlineData("https://www.GitHub.com/the-onyx-team?tab=1", "github.com")]
    [InlineData("http://localhost:5262/home", "localhost")]
    [InlineData("mail.example.com.", "mail.example.com")]
    [InlineData("https://bücher.de/", "xn--bcher-kva.de")]
    [InlineData("192.168.0.1", "192.168.0.1")]
    [InlineData("chrome://settings", null)]
    [InlineData("file:///home/user/a.html", null)]
    [InlineData("not a domain", null)]
    [InlineData("-bad-.com", null)]
    [InlineData("", null)]
    public void Normalize(string input, string? expected) => WebDomain.Normalize(input).ShouldBe(expected);

    [Fact]
    public void IsExcluded_CoversSubdomainsOnly()
    {
        WebDomain.IsExcluded("example.com", ["example.com"]).ShouldBeTrue();
        WebDomain.IsExcluded("a.b.example.com", ["example.com"]).ShouldBeTrue();
        WebDomain.IsExcluded("notexample.com", ["example.com"]).ShouldBeFalse();
    }
}

[TestSubject(typeof(WebsiteSettings))]
public sealed class WebsiteTest : IDisposable
{
    private static readonly DateTime s_Hour = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection m_Connection;
    private readonly ApplicationDbContext m_Db;
    private readonly CurrentUser m_User;
    private readonly int m_DeviceId;

    public WebsiteTest()
    {
        m_Connection = new SqliteConnection("Data Source=:memory:");
        m_Connection.Open();
        m_Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(m_Connection).Options);
        m_Db.Database.EnsureCreated();

        m_Db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "one" });
        var device = new Device { Name = "Laptop", UserId = "user-1" };
        m_Db.Devices.Add(device);
        m_Db.SaveChanges();
        m_DeviceId = device.Id;

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"))
            }
        };
        m_User = new CurrentUser(accessor, new ServiceCollection().BuildServiceProvider());
    }

    public void Dispose()
    {
        m_Db.Dispose();
        m_Connection.Dispose();
    }

    private UsageDataService Usage() =>
        new(m_Db, m_User, new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 30, 0, TimeSpan.Zero)));

    private WebsiteSettings Settings() => new(m_Db, m_User);

    private static UsageEntryDto Firefox(int minutes, string? domain, DateTime? hour = null) =>
        new("linux:firefox", "Firefox", "Network", hour ?? s_Hour, TimeSpan.FromMinutes(minutes), domain);

    private Dictionary<string, TimeSpan> Stored() =>
        m_Db.Usages.AsNoTracking().ToList().ToDictionary(u => u.Domain, u => u.Duration);

    [Fact]
    public async Task Upload_StoresOneRowPerWebsite_AndReplacesOnReupload()
    {
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(20, "github.com"), Firefox(5, null), Firefox(3, "https://www.github.com/x")]);
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(25, "github.com"), Firefox(5, null)]);

        var stored = Stored();
        stored.Count.ShouldBe(2);
        stored["github.com"].ShouldBe(TimeSpan.FromMinutes(25));
        stored[""].ShouldBe(TimeSpan.FromMinutes(5));

        var read = await Usage().GetUsageDataAsync(s_Hour, s_Hour.AddHours(1));
        read.Select(u => u.Domain).ShouldBe(["github.com", null], ignoreOrder: true);
    }

    [Fact]
    public async Task Upload_ExcludedWebsite_KeepsTheTimeWithoutTheDomain()
    {
        await Settings().SetExcludedDomainsAsync(["example.com"]);

        await Usage().UploadUsageAsync(m_DeviceId,
            [Firefox(10, "mail.example.com"), Firefox(4, "example.com"), Firefox(5, null), Firefox(7, "github.com"), Firefox(1, "not a domain")]);

        var stored = Stored();
        stored.Keys.ShouldBe(["", "github.com"], ignoreOrder: true);
        stored[""].ShouldBe(TimeSpan.FromMinutes(10 + 4 + 5 + 1));
    }

    [Fact]
    public async Task SetExcludedDomains_NormalizesAndRemovesExistingHistory()
    {
        await Usage().UploadUsageAsync(m_DeviceId,
            [Firefox(10, "docs.example.com"), Firefox(5, null), Firefox(7, "github.com"), Firefox(3, "example.com", s_Hour.AddHours(-1))]);

        var stored = await Settings().SetExcludedDomainsAsync(["https://www.Example.com/", "example.com"]);

        stored.ShouldBe(["example.com"]);
        var rows = m_Db.Usages.AsNoTracking().ToList();
        rows.ShouldNotContain(u => u.Domain.EndsWith("example.com"));
        rows.Single(u => u.Domain == "" && u.Date == s_Hour).Duration.ShouldBe(TimeSpan.FromMinutes(15));
        rows.Single(u => u.Domain == "" && u.Date == s_Hour.AddHours(-1)).Duration.ShouldBe(TimeSpan.FromMinutes(3));
        (await Settings().GetExcludedDomainsAsync()).ShouldBe(["example.com"]);
    }

    [Fact]
    public async Task SetExcludedDomains_RejectsInvalidEntries()
    {
        await Should.ThrowAsync<ArgumentException>(() => Settings().SetExcludedDomainsAsync(["github.com", "not a domain"]));
        (await Settings().GetExcludedDomainsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteHistory_KeepsBrowserTime()
    {
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(40, "github.com"), Firefox(30, "example.org"), Firefox(5, null)]);

        await Settings().DeleteWebsiteHistoryAsync();

        var stored = Stored();
        stored.Keys.ShouldBe([""]);
        // 75 minutes of reports for one hour are capped at the hour.
        stored[""].ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Upload_ReplacesTheWholeHour_RemovingWebsitesNoLongerSent()
    {
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(20, "github.com"), Firefox(10, "example.org")]);
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(25, "github.com")]);

        Stored().ShouldBe(new Dictionary<string, TimeSpan> { ["github.com"] = TimeSpan.FromMinutes(25) });
    }

    [Fact]
    public async Task DeleteHistory_ThenCollectorResendsTheHour_DoesNotCountTwice()
    {
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(30, "github.com")]);
        await Settings().DeleteWebsiteHistoryAsync();

        // The collector still has the current hour and sends it again on its next sync.
        await Usage().UploadUsageAsync(m_DeviceId, [Firefox(32, "github.com")]);

        m_Db.Usages.AsNoTracking().ToList().Sum(u => u.Duration.TotalMinutes).ShouldBe(32);
    }
}
