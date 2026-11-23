using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Onyx.App.Web.Api;
using Onyx.App.Web.Services.Auth;
using Onyx.App.Web.Services.Data;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

/// <summary>An in-memory database with two users, each with a device.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection m_Connection = new("Data Source=:memory:");

    public TestDatabase()
    {
        m_Connection.Open();
        Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(m_Connection).Options);
        Db.Database.EnsureCreated();
        foreach (var name in new[] { "alice", "bob", "carol" })
        {
            Db.Users.Add(new ApplicationUser
            {
                Id = name, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
                Email = $"{name}@example.com", NormalizedEmail = $"{name}@example.com".ToUpperInvariant()
            });
            Db.Devices.Add(new Device { Name = $"{name}'s laptop", Platform = "Linux", UserId = name });
        }

        Db.SaveChanges();
    }

    public ApplicationDbContext Db { get; }

    public int DeviceOf(string user) => Db.Devices.Single(d => d.UserId == user).Id;

    /// <remarks>Not <see cref="HttpContextAccessor"/>: it keeps the context in a static AsyncLocal, so a second user
    /// would replace the first.</remarks>
    public static CurrentUser As(string userId) => new(new FixedAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
        }
    });

    public void Dispose()
    {
        Db.Dispose();
        m_Connection.Dispose();
    }

    private sealed class FixedAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}

[TestSubject(typeof(E2EEndpoints))]
public class E2EEndpointsTest : IDisposable
{
    private readonly TestDatabase m_Test = new();
    private ApplicationDbContext Db => m_Test.Db;
    private static readonly TimeProvider s_Time = TimeProvider.System;

    public void Dispose() => m_Test.Dispose();

    private static CreateVaultDto Vault() => new(new byte[16], 600_000, new byte[61],
        [0x04, .. new byte[64]], new byte[61], [1, 2, 3]);

    private static string Tag(char c) => new(c, 22);

    private static readonly DateTime s_Hour = DateTime.UtcNow.Date.AddHours(-30);

    [Fact]
    public async Task Vault_IsCreatedOnce_AndReturnedToItsOwnerOnly()
    {
        (await E2EEndpoints.GetVaultAsync(Db, TestDatabase.As("alice"))).ShouldBeOfType<NotFound<string>>();

        (await E2EEndpoints.CreateVaultAsync(Vault(), Db, TestDatabase.As("alice"), s_Time)).ShouldBeOfType<Created>();
        (await E2EEndpoints.CreateVaultAsync(Vault(), Db, TestDatabase.As("alice"), s_Time)).ShouldBeOfType<Conflict<string>>();

        var ok = (await E2EEndpoints.GetVaultAsync(Db, TestDatabase.As("alice"))).ShouldBeOfType<Ok<VaultDto>>();
        ok.Value!.SettingsVersion.ShouldBe(1);
        (await E2EEndpoints.GetVaultAsync(Db, TestDatabase.As("bob"))).ShouldBeOfType<NotFound<string>>();
    }

    [Fact]
    public async Task Vault_RejectsWeakKeyParameters()
    {
        (await E2EEndpoints.CreateVaultAsync(Vault() with { Iterations = 1000 }, Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await E2EEndpoints.CreateVaultAsync(Vault() with { PublicKey = new byte[65] }, Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task Settings_OnlyReplaceTheVersionTheyWereBasedOn()
    {
        await E2EEndpoints.CreateVaultAsync(Vault(), Db, TestDatabase.As("alice"), s_Time);

        var first = await E2EEndpoints.UpdateSettingsAsync(new UpdateVaultSettingsDto([9], 1), Db, TestDatabase.As("alice"), s_Time);
        first.ShouldBeOfType<Ok<VaultSettingsVersionDto>>().Value!.SettingsVersion.ShouldBe(2);

        // A second device still on version 1 must not overwrite the change.
        (await E2EEndpoints.UpdateSettingsAsync(new UpdateVaultSettingsDto([8], 1), Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<Conflict<string>>();

        Db.ChangeTracker.Clear();
        Db.UserVaults.Single().Settings.ShouldBe(new byte[] { 9 });
    }

    [Fact]
    public async Task Upload_ReplacesAnAppsHour_AndAsksForMissingAppInfo()
    {
        var device = m_Test.DeviceOf("alice");
        var alice = TestDatabase.As("alice");

        var first = await E2EEndpoints.UploadUsageAsync(device, new EncryptedUsageUploadDto(
            [new(Tag('a'), s_Hour, [1]), new(Tag('b'), s_Hour, [2])]), Db, alice, s_Time);
        first.ShouldBeOfType<Ok<EncryptedUsageUploadResultDto>>().Value!.TagsMissingInfo.Order().ShouldBe([Tag('a'), Tag('b')]);

        await E2EEndpoints.PutAppsAsync([new EncryptedAppDto(Tag('a'), [5])], createOnly: true, Db, alice, s_Time);
        var second = await E2EEndpoints.UploadUsageAsync(device, new EncryptedUsageUploadDto(
            [new(Tag('a'), s_Hour, [3])]), Db, alice, s_Time);
        second.ShouldBeOfType<Ok<EncryptedUsageUploadResultDto>>().Value!.TagsMissingInfo.ShouldBeEmpty();

        Db.ChangeTracker.Clear();
        Db.EncryptedUsages.Count().ShouldBe(2);
        Db.EncryptedUsages.Single(u => u.Tag == Tag('a')).Data.ShouldBe(new byte[] { 3 });

        var rows = (await E2EEndpoints.GetUsageAsync(s_Hour.AddHours(-1), s_Hour.AddHours(1), null, Db, alice))
            .ShouldBeOfType<Ok<IEnumerable<EncryptedUsageDto>>>().Value!.ToList();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.Hour.Kind == DateTimeKind.Utc && r.DeviceId == device);
    }

    [Fact]
    public async Task Upload_ToAnotherUsersDevice_IsNotFound_AndTheirDataIsInvisible()
    {
        var aliceDevice = m_Test.DeviceOf("alice");
        await E2EEndpoints.UploadUsageAsync(aliceDevice, new EncryptedUsageUploadDto([new(Tag('a'), s_Hour, [1])]),
            Db, TestDatabase.As("alice"), s_Time);

        (await E2EEndpoints.UploadUsageAsync(aliceDevice, new EncryptedUsageUploadDto([new(Tag('a'), s_Hour, [2])]),
            Db, TestDatabase.As("bob"), s_Time)).ShouldBeOfType<NotFound<string>>();

        var bobSees = (await E2EEndpoints.GetUsageAsync(s_Hour.AddDays(-1), s_Hour.AddDays(1), aliceDevice, Db,
            TestDatabase.As("bob"))).ShouldBeOfType<Ok<IEnumerable<EncryptedUsageDto>>>();
        bobSees.Value!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Upload_RejectsMalformedRows()
    {
        var device = m_Test.DeviceOf("alice");
        var alice = TestDatabase.As("alice");
        (await E2EEndpoints.UploadUsageAsync(device, new EncryptedUsageUploadDto([new("short", s_Hour, [1])]), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await E2EEndpoints.UploadUsageAsync(device, new EncryptedUsageUploadDto([new(Tag('a'), s_Hour.AddMinutes(5), [1])]), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await E2EEndpoints.UploadUsageAsync(device, new EncryptedUsageUploadDto([new(Tag('a'), DateTime.UtcNow.AddDays(2).Date, [1])]), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await E2EEndpoints.GetUsageAsync(s_Hour, s_Hour.AddDays(500), null, Db, alice)).ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task CreateOnly_KeepsTheUsersOwnAppInfo()
    {
        var alice = TestDatabase.As("alice");
        await E2EEndpoints.PutAppsAsync([new EncryptedAppDto(Tag('a'), [1])], createOnly: false, Db, alice, s_Time);
        await E2EEndpoints.PutAppsAsync([new EncryptedAppDto(Tag('a'), [2])], createOnly: true, Db, alice, s_Time);
        Db.ChangeTracker.Clear();
        Db.EncryptedApps.Single().Data.ShouldBe(new byte[] { 1 });

        await E2EEndpoints.PutAppsAsync([new EncryptedAppDto(Tag('a'), [3])], createOnly: false, Db, alice, s_Time);
        Db.ChangeTracker.Clear();
        Db.EncryptedApps.Single().Data.ShouldBe(new byte[] { 3 });
    }

    [Fact]
    public async Task Legacy_PlainTextIsRefusedOnceEncrypted_AndDeletedAfterTheMove()
    {
        var alice = TestDatabase.As("alice");
        var device = m_Test.DeviceOf("alice");
        var usage = new UsageDataService(Db, alice, s_Time);
        await usage.UploadUsageAsync(device, [new UsageEntryDto("linux:firefox", "Firefox", null, s_Hour, TimeSpan.FromMinutes(5))]);
        // Bob uses the same app: it must stay in the shared app list.
        await new UsageDataService(Db, TestDatabase.As("bob"), s_Time).UploadUsageAsync(m_Test.DeviceOf("bob"),
            [new UsageEntryDto("linux:firefox", "Firefox", null, s_Hour, TimeSpan.FromMinutes(5))]);
        await usage.UploadUsageAsync(device, [new UsageEntryDto("linux:only-alice", "Mine", null, s_Hour, TimeSpan.FromMinutes(5))]);

        var status = (await E2EEndpoints.GetLegacyStatusAsync(Db, alice)).ShouldBeOfType<Ok<LegacyStatusDto>>().Value!;
        status.UsageRows.ShouldBe(2);
        status.Apps.ShouldBe(2);

        // Deleting before encryption is set up would lose the data.
        (await E2EEndpoints.DeleteLegacyAsync(Db, alice)).ShouldBeOfType<BadRequest<string>>();

        await E2EEndpoints.CreateVaultAsync(Vault(), Db, alice, s_Time);
        var refused = await DeviceEndpoints.UploadUsageAsync(device,
            new UsageUploadDto([new UsageEntryDto("linux:firefox", "Firefox", null, s_Hour, TimeSpan.FromMinutes(5))]),
            usage, Db, alice);
        refused.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status409Conflict);

        (await E2EEndpoints.DeleteLegacyAsync(Db, alice)).ShouldBeOfType<NoContent>();
        Db.ChangeTracker.Clear();
        Db.Usages.Count(u => u.Device!.UserId == "alice").ShouldBe(0);
        Db.Usages.Count(u => u.Device!.UserId == "bob").ShouldBe(1);
        Db.RegisteredApps.Select(a => a.Identifier).ToList().ShouldBe(["linux:firefox"]);
    }

    [Fact]
    public async Task DeletingTheDevice_DeletesItsEncryptedUsage()
    {
        var device = m_Test.DeviceOf("alice");
        await E2EEndpoints.UploadUsageAsync(device, new EncryptedUsageUploadDto([new(Tag('a'), s_Hour, [1])]),
            Db, TestDatabase.As("alice"), s_Time);

        await new DeviceManager(Db, TestDatabase.As("alice")).UnregisterDeviceAsync(device);
        Db.ChangeTracker.Clear();
        Db.EncryptedUsages.Count().ShouldBe(0);
    }
}
