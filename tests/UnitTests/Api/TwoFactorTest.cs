using System.Security.Claims;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Onyx.App.Web.Api;
using Onyx.App.Web.Services;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

/// <summary>Two-factor authentication with the real Identity stack (TOTP keys, recovery codes) on SQLite.</summary>
[TestSubject(typeof(TwoFactorAuth))]
public sealed class TwoFactorTest : IDisposable
{
    private readonly SqliteConnection m_Connection = new("Data Source=:memory:");
    private readonly ServiceProvider m_Services;
    private readonly UserManager<ApplicationUser> m_Users;
    private readonly KeyAccessor m_Keys = new(new RsaSecurityKey(RSA.Create()));
    private readonly ApplicationUser m_User;
    private readonly DefaultHttpContext m_Context;

    public TwoFactorTest()
    {
        m_Connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(m_Connection));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        m_Services = services.BuildServiceProvider();
        m_Services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        m_Users = m_Services.GetRequiredService<UserManager<ApplicationUser>>();

        m_User = new ApplicationUser { UserName = "alice", Email = "alice@example.com" };
        m_Users.CreateAsync(m_User, "Password123!").GetAwaiter().GetResult().Succeeded.ShouldBeTrue();
        m_Context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, m_User.Id)], "test"))
        };
    }

    public void Dispose()
    {
        m_Services.Dispose();
        m_Connection.Dispose();
    }

    private async Task<string> KeyAsync()
    {
        var result = await AuthEndpoints.AuthenticatorKeyHandler(m_Context, m_Users);
        var dto = result.ShouldBeOfType<Ok<TwoFactorKeyDto>>().Value!;
        dto.Uri.ShouldStartWith("otpauth://totp/Onyx:alice%40example.com?secret=");
        return dto.Key.Replace(" ", "").ToUpperInvariant();
    }

    /// <summary>The code an authenticator app shows now (RFC 6238: HMAC-SHA1, 30 s, 6 digits).</summary>
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.TrimEnd('=').Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private async Task<IReadOnlyList<string>> EnableAsync()
    {
        var key = await KeyAsync();
        var result = await AuthEndpoints.Enable2FaHandler(new TwoFactorCodeDto(Totp(key)), m_Context, m_Users);
        return result.ShouldBeOfType<Ok<RecoveryCodesDto>>().Value!.Codes;
    }

    private Task<IResult> LoginAsync(string? code) =>
        AuthEndpoints.LoginHandler(new LoginDto("alice@example.com", "Password123!", code), m_Users, m_Keys);

    [Fact]
    public async Task Enable_NeedsACodeFromTheApp_AndReturnsRecoveryCodes()
    {
        await KeyAsync();
        var wrong = await AuthEndpoints.Enable2FaHandler(new TwoFactorCodeDto("000000"), m_Context, m_Users);
        wrong.ShouldBeOfType<BadRequest<string>>();

        var codes = await EnableAsync();

        codes.Count.ShouldBe(TwoFactorAuth.RecoveryCodeCount);
        (await m_Users.GetTwoFactorEnabledAsync(m_User)).ShouldBeTrue();
        var status = await AuthEndpoints.TwoFactorStatusHandler(m_Context, m_Users);
        status.ShouldBeOfType<Ok<TwoFactorStatusDto>>().Value.ShouldBe(new TwoFactorStatusDto(true, 10));
    }

    [Fact]
    public async Task Login_WithTwoFactor_AsksForTheCode_AndAcceptsTheAppOrARecoveryCodeOnce()
    {
        (await LoginAsync(null)).ShouldBeOfType<Ok<LoginResultDto>>(); // off: password is enough

        var codes = await EnableAsync();
        var key = await KeyAsync();

        (await LoginAsync(null)).ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(TwoFactor.RequiredStatus);
        (await LoginAsync("123 456")).ShouldBeOfType<BadRequest<string>>();
        (await LoginAsync(Totp(key))).ShouldBeOfType<Ok<LoginResultDto>>();

        // Recovery codes work once, typed in any case and without the hyphen.
        var recovery = codes[0];
        (await LoginAsync(recovery.Replace("-", "").ToLowerInvariant())).ShouldBeOfType<Ok<LoginResultDto>>();
        (await LoginAsync(recovery)).ShouldBeOfType<BadRequest<string>>();
        (await m_Users.CountRecoveryCodesAsync(m_User)).ShouldBe(9);
    }

    [Fact]
    public async Task Disable_NeedsACode_AndForgetsTheKey()
    {
        await EnableAsync();
        var key = await KeyAsync();

        (await AuthEndpoints.Disable2FaHandler(new TwoFactorCodeDto("000000"), m_Context, m_Users))
            .ShouldBeOfType<BadRequest<string>>();
        (await AuthEndpoints.Disable2FaHandler(new TwoFactorCodeDto(Totp(key)), m_Context, m_Users))
            .ShouldBeOfType<Ok>();

        (await m_Users.GetTwoFactorEnabledAsync(m_User)).ShouldBeFalse();
        (await KeyAsync()).ShouldNotBe(key);
        (await LoginAsync(null)).ShouldBeOfType<Ok<LoginResultDto>>();
    }

    [Theory]
    [InlineData("ABCDEFGHIJKLMNOP", "abcd efgh ijkl mnop")]
    [InlineData("ABCDEF", "abcd ef")]
    public void FormatKey_GroupsByFour(string key, string formatted) =>
        TwoFactorAuth.FormatKey(key).ShouldBe(formatted);
}
