using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Onyx.App.Shared.Services.Integrations;
using Onyx.App.Web.Api;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

[TestSubject(typeof(IntegrationEndpoints))]
public class IntegrationEndpointsTest : IDisposable
{
    private readonly TestDatabase m_Test = new();
    private ApplicationDbContext Db => m_Test.Db;
    private static readonly TimeProvider s_Time = TimeProvider.System;
    private static readonly IOnyxCrypto s_Crypto = DotNetCrypto.Instance;

    public void Dispose() => m_Test.Dispose();

    private async Task<(IntegrationKey Key, string Token)> CreateAsync(string user)
    {
        var key = IntegrationKey.Create();
        var token = await key.AccessTokenAsync(s_Crypto);
        (await IntegrationEndpoints.CreateAsync(new CreateIntegrationDto(key.Id, token), Db, TestDatabase.As(user), s_Time))
            .ShouldBeOfType<Created>();
        return (key, token);
    }

    private static HttpRequest Bearer(string credential)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + credential;
        return context.Request;
    }

    [Fact]
    public async Task TheIntegrationReadsWhatTheOwnerPublished_WithItsTokenOnly()
    {
        var (key, token) = await CreateAsync("alice");

        // Nothing published yet.
        (await IntegrationEndpoints.GetDataAsync(Bearer($"{key.Id}.{token}"), Db, s_Time))
            .ShouldBeOfType<Ok<IntegrationExportDto>>().Value!.Data.ShouldBeNull();

        var blob = await s_Crypto.EncryptAsync(await key.EncryptionKeyAsync(s_Crypto), "{\"format\":1}"u8.ToArray(), key.Context);
        (await IntegrationEndpoints.PutDataAsync(key.Id, new IntegrationDataDto(blob), Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<NoContent>();

        var export = (await IntegrationEndpoints.GetDataAsync(Bearer($"{key.Id}.{token}"), Db, s_Time))
            .ShouldBeOfType<Ok<IntegrationExportDto>>().Value!;
        export.UpdatedAt.ShouldNotBeNull();
        (await s_Crypto.DecryptAsync(await key.EncryptionKeyAsync(s_Crypto), export.Data!, key.Context))
            .ShouldBe("{\"format\":1}"u8.ToArray());

        // The server never stored the token itself, and a wrong or missing one is refused.
        Db.Integrations.Single().TokenHash.ShouldBe(IntegrationKey.HashToken(token));
        (await IntegrationEndpoints.GetDataAsync(Bearer($"{key.Id}.{token[..^1]}x"), Db, s_Time)).ShouldBeOfType<UnauthorizedHttpResult>();
        (await IntegrationEndpoints.GetDataAsync(Bearer(token), Db, s_Time)).ShouldBeOfType<UnauthorizedHttpResult>();
        (await IntegrationEndpoints.GetDataAsync(new DefaultHttpContext().Request, Db, s_Time)).ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task OthersCannotPublishListOrDelete()
    {
        var (key, _) = await CreateAsync("alice");
        var bob = TestDatabase.As("bob");

        (await IntegrationEndpoints.PutDataAsync(key.Id, new IntegrationDataDto(new byte[64]), Db, bob, s_Time)).ShouldBeOfType<NotFound>();
        (await IntegrationEndpoints.DeleteAsync(key.Id, Db, bob)).ShouldBeOfType<NotFound>();
        (await IntegrationEndpoints.ListAsync(Db, bob)).ShouldBeOfType<Ok<List<IntegrationDto>>>().Value!.ShouldBeEmpty();
        (await IntegrationEndpoints.ListAsync(Db, TestDatabase.As("alice")))
            .ShouldBeOfType<Ok<List<IntegrationDto>>>().Value!.ShouldHaveSingleItem().Id.ShouldBe(key.Id);

        (await IntegrationEndpoints.DeleteAsync(key.Id, Db, TestDatabase.As("alice"))).ShouldBeOfType<NoContent>();
        Db.Integrations.Count().ShouldBe(0);
    }

    [Fact]
    public async Task RejectsMalformedIdsTokensAndSnapshots()
    {
        var alice = TestDatabase.As("alice");
        var token = new string('a', 43);
        (await IntegrationEndpoints.CreateAsync(new CreateIntegrationDto("short", token), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await IntegrationEndpoints.CreateAsync(new CreateIntegrationDto(new string('/', 22), token), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await IntegrationEndpoints.CreateAsync(new CreateIntegrationDto(new string('a', 22), "x"), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();

        var (key, _) = await CreateAsync("alice");
        (await IntegrationEndpoints.CreateAsync(new CreateIntegrationDto(key.Id, token), Db, TestDatabase.As("bob"), s_Time))
            .ShouldBeOfType<Conflict>();
        (await IntegrationEndpoints.PutDataAsync(key.Id, new IntegrationDataDto([1, 2]), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
        (await IntegrationEndpoints.PutDataAsync(key.Id, new IntegrationDataDto(new byte[IntegrationEndpoints.MaxSnapshotBytes + 1]), Db, alice, s_Time))
            .ShouldBeOfType<BadRequest<string>>();
    }
}

[TestSubject(typeof(IntegrationKey))]
public class IntegrationKeyTest
{
    [Fact]
    public async Task KeyText_RoundTrips_AndDerivesSeparateTokenAndKey()
    {
        var key = IntegrationKey.Create();
        var text = key.ToString();
        text.ShouldStartWith("onyx1.");

        IntegrationKey.TryParse(" " + text + "\n", out var parsed).ShouldBeTrue();
        parsed.Id.ShouldBe(key.Id);
        parsed.Secret.ShouldBe(key.Secret);

        var token = await key.AccessTokenAsync(DotNetCrypto.Instance);
        token.Length.ShouldBe(43);
        IntegrationKey.FromBase64Url(token).ShouldNotBe(await key.EncryptionKeyAsync(DotNetCrypto.Instance));

        IntegrationKey.TryParse("onyx1.abc.def", out _).ShouldBeFalse();
        IntegrationKey.TryParse(text.Replace("onyx1", "onyx2"), out _).ShouldBeFalse();
        IntegrationKey.TryParse(null, out _).ShouldBeFalse();
    }
}

public class IntegrationKeyVectorTest
{
    /// <summary>The values of docs/Writerside/topics/Integrations.md: integrations in other languages derive the
    /// same token and key.</summary>
    [Fact]
    public async Task Derivation_MatchesTheDocumentedVector()
    {
        IntegrationKey.TryParse("onyx1.AAAAAAAAAAAAAAAAAAAAAA.AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", out var key).ShouldBeTrue();
        (await key.AccessTokenAsync(DotNetCrypto.Instance)).ShouldBe("jeE-wtBjgzR3jyACy-m8m2BgExaZ78J4QOde3CXd_0s");
        Convert.ToHexStringLower(await key.EncryptionKeyAsync(DotNetCrypto.Instance))
            .ShouldBe("64d537f4cfb6a99155f9667d5f554b17e86fa8b493a4c6243faf9a7b06ff1748");
    }
}

[TestSubject(typeof(IntegrationSnapshotBuilder))]
public class IntegrationSnapshotBuilderTest
{
    private static readonly TimeZoneInfo s_Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private static UsageDto Usage(string app, DateTime utc, int minutes, string? domain = null, int opens = 0) => new()
    {
        AppId = app.GetHashCode(), AppName = app, Category = "Social", Date = utc, TimeSpan = TimeSpan.FromMinutes(minutes),
        DeviceId = 1, DeviceName = "Phone", Domain = domain, Opens = opens
    };

    [Fact]
    public void Build_GivesOneEntryPerDay_InTheUsersZone_WithOnlyTheAllowedLists()
    {
        var today = new DateOnly(2026, 11, 28);
        var settings = new IntegrationSettings { Days = 2, Apps = true, Categories = false, Websites = true, Devices = false };
        UsageDto[] usage =
        [
            Usage("Chat", new DateTime(2026, 11, 27, 23, 0, 0, DateTimeKind.Utc), 30, opens: 3), // 00:00 on the 28th in Vienna
            Usage("Firefox", new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc), 20, opens: 1),
            Usage("Firefox", new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc), 10, "github.com", opens: 4),
            Usage("Chat", new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc), 99) // before the range
        ];
        DeviceUnlocks[] unlocks = [new(1, "Phone", new DateTime(2026, 11, 28, 6, 0, 0, DateTimeKind.Utc), 12)];

        var snapshot = IntegrationSnapshotBuilder.Build(settings, usage, unlocks, s_Vienna, today, DateTimeOffset.UtcNow);

        snapshot.From.ShouldBe(new DateOnly(2026, 11, 27));
        snapshot.Days.Select(d => d.Date).ShouldBe([new DateOnly(2026, 11, 27), today]);
        var yesterday = snapshot.Days[0];
        yesterday.ScreenTimeMinutes.ShouldBe(30);
        yesterday.AppOpens.ShouldBe(1);
        yesterday.Apps!.ShouldHaveSingleItem().Opens.ShouldBe(1);
        yesterday.Websites!.ShouldHaveSingleItem().ShouldBe(new IntegrationWebsite("github.com", 10, 4));
        yesterday.Categories.ShouldBeNull();
        yesterday.Devices.ShouldBeNull();
        snapshot.Days[1].ScreenTimeMinutes.ShouldBe(30);
        snapshot.Days[1].Unlocks.ShouldBe(12);
    }
}
