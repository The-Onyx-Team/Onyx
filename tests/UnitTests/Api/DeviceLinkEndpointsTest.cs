using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Onyx.App.Web.Api;
using Onyx.App.Web.Services.Auth;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

[TestSubject(typeof(DeviceLinkEndpoints))]
public class DeviceLinkEndpointsTest : IDisposable
{
    private readonly TestDatabase m_Test = new();
    private readonly MemoryCache m_Cache = new(new MemoryCacheOptions());
    private readonly KeyAccessor m_Keys = new(new RsaSecurityKey(RSA.Create()));
    private static readonly IOnyxCrypto s_Crypto = DotNetCrypto.Instance;
    private static readonly TimeProvider s_Time = TimeProvider.System;

    public void Dispose()
    {
        m_Cache.Dispose();
        m_Test.Dispose();
    }

    [Fact]
    public async Task TheWatchGetsTokensOfTheUser_AndAKeyOnlyItCanDecrypt()
    {
        // The watch.
        var (watchPrivate, watchPublic) = await s_Crypto.CreateKeyPairAsync();
        var started = (await DeviceLinkEndpoints.StartAsync(new StartLinkDto(watchPublic), m_Cache, s_Time))
            .ShouldBeOfType<Ok<LinkStartedDto>>().Value!;
        started.Code.ShouldBe(DeviceLink.CodeOf(watchPublic));
        DeviceLinkEndpoints.Poll(started.Code, new PollLinkDto(started.PollToken), m_Cache).ShouldBeOfType<Accepted>();

        // The phone, signed in as alice, with the code typed sloppily.
        var typed = DeviceLink.Display(started.Code).ToLowerInvariant();
        var info = DeviceLinkEndpoints.Get(typed, m_Cache).ShouldBeOfType<Ok<LinkInfoDto>>().Value!;
        DeviceLink.CodeOf(info.PublicKey).ShouldBe(started.Code);
        var masterKey = RandomNumberGenerator.GetBytes(32);
        var (phonePrivate, phonePublic) = await s_Crypto.CreateKeyPairAsync();
        var shared = await s_Crypto.SharedKeyAsync(phonePrivate, phonePublic, info.PublicKey);
        var encrypted = await s_Crypto.EncryptAsync(shared, masterKey, DeviceLink.Context(started.Code));
        (await DeviceLinkEndpoints.CompleteAsync(typed, new CompleteLinkDto(phonePublic, encrypted), m_Cache,
            m_Test.Db, TestDatabase.As("alice"), m_Keys)).ShouldBeOfType<NoContent>();

        // Someone without the poll token gets nothing; the watch gets the result once.
        DeviceLinkEndpoints.Poll(started.Code, new PollLinkDto("guess"), m_Cache).ShouldBeOfType<NotFound>();
        var result = DeviceLinkEndpoints.Poll(started.Code, new PollLinkDto(started.PollToken), m_Cache)
            .ShouldBeOfType<Ok<LinkResultDto>>().Value!;
        DeviceLinkEndpoints.Poll(started.Code, new PollLinkDto(started.PollToken), m_Cache).ShouldBeOfType<NotFound>();

        new JwtSecurityTokenHandler().ReadJwtToken(result.Token).Claims
            .ShouldContain(c => c.Type == "nameid" && c.Value == "alice");
        var watchShared = await s_Crypto.SharedKeyAsync(watchPrivate, watchPublic, result.PublicKey);
        (await s_Crypto.DecryptAsync(watchShared, result.EncryptedKey, DeviceLink.Context(started.Code))).ShouldBe(masterKey);
    }

    [Fact]
    public async Task UnknownCodesAndBadKeysAreRefused()
    {
        DeviceLinkEndpoints.Get("0000000000", m_Cache).ShouldBeOfType<NotFound>();
        (await DeviceLinkEndpoints.StartAsync(new StartLinkDto(new byte[33]), m_Cache, s_Time)).ShouldBeOfType<BadRequest<string>>();
        (await DeviceLinkEndpoints.CompleteAsync("0000000000", new CompleteLinkDto(new byte[65], new byte[60]), m_Cache,
            m_Test.Db, TestDatabase.As("alice"), m_Keys)).ShouldBeOfType<NotFound>();
    }
}

[TestSubject(typeof(DeviceLink))]
public class DeviceLinkTest
{
    [Fact]
    public void Codes_AreStableReadableAndForgiving()
    {
        var key = new byte[65];
        key[0] = 4;
        var code = DeviceLink.CodeOf(key);

        code.Length.ShouldBe(DeviceLink.CodeLength);
        code.ShouldBe(DeviceLink.CodeOf(key));
        code.ShouldNotContain('I');
        code.ShouldNotContain('O');
        DeviceLink.Display(code).ShouldBe($"{code[..5]}-{code[5..]}");
        DeviceLink.Normalize(" " + DeviceLink.Display(code).ToLowerInvariant() + " ").ShouldBe(code);
        DeviceLink.Normalize("o1-il").ShouldBe("0111");

        key[64] = 1;
        DeviceLink.CodeOf(key).ShouldNotBe(code);
    }
}
