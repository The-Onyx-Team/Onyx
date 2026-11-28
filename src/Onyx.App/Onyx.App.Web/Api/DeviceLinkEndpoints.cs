using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Onyx.App.Web.Services.Auth;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;

namespace Onyx.App.Web.Api;

/// <summary>
/// Linking devices without a keyboard (watches) by code, see <see cref="DeviceLink"/>. Pending links only live in
/// memory for <see cref="DeviceLink.Lifetime"/>; a server restart simply makes the device ask for a new code.
/// </summary>
public static class DeviceLinkEndpoints
{
    private sealed class PendingLink(byte[] publicKey, byte[] pollTokenHash)
    {
        public byte[] PublicKey { get; } = publicKey;
        public byte[] PollTokenHash { get; } = pollTokenHash;
        public LinkResultDto? Result { get; set; }
    }

    private static string CacheKey(string code) => "device-link:" + code;

    public static IEndpointRouteBuilder MapDeviceLinkEndpoints(this IEndpointRouteBuilder routes)
    {
        // The new device has no account yet.
        var device = routes.MapGroup("/api/link")
            .WithTags("Device linking")
            .AllowAnonymous()
            .RequireRateLimiting(IntegrationEndpoints.RateLimit);
        device.MapPost("/", StartAsync).Produces<LinkStartedDto>();
        device.MapPost("/{code}/poll", Poll).Produces<LinkResultDto>();

        var user = routes.MapGroup("/api/link")
            .WithTags("Device linking")
            .RequireAuthorization(Program.ApiPolicy);
        user.MapGet("/{code}", Get).Produces<LinkInfoDto>();
        user.MapPost("/{code}/complete", CompleteAsync);

        return routes;
    }

    public static Task<IResult> StartAsync([FromBody] StartLinkDto dto, IMemoryCache cache, TimeProvider time)
    {
        if (dto.PublicKey is not { Length: 65 } || dto.PublicKey[0] != 0x04)
            return Task.FromResult(Results.BadRequest("The public key must be an uncompressed P-256 point."));

        var code = DeviceLink.CodeOf(dto.PublicKey);
        var pollToken = IntegrationKey.Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = time.GetUtcNow().Add(DeviceLink.Lifetime);
        cache.Set(CacheKey(code), new PendingLink(dto.PublicKey, Hash(pollToken)), expires);
        return Task.FromResult(Results.Ok(new LinkStartedDto(code, pollToken, expires.UtcDateTime)));
    }

    /// <summary>The public key behind a code, for the signed-in device to check against the code.</summary>
    public static IResult Get([FromRoute] string code, IMemoryCache cache) =>
        cache.Get<PendingLink>(CacheKey(DeviceLink.Normalize(code))) is { Result: null } link
            ? Results.Ok(new LinkInfoDto(link.PublicKey))
            : Results.NotFound();

    /// <summary>Gives the new device tokens of the signed-in user, with the master key the client encrypted for it.</summary>
    public static async Task<IResult> CompleteAsync([FromRoute] string code, [FromBody] CompleteLinkDto dto,
        IMemoryCache cache, ApplicationDbContext db, CurrentUser currentUser, KeyAccessor keys)
    {
        if (dto.PublicKey is not { Length: 65 } || dto.EncryptedKey is not { Length: > CryptoFormat.Overhead and <= 256 })
            return Results.BadRequest("Invalid key.");
        if (cache.Get<PendingLink>(CacheKey(DeviceLink.Normalize(code))) is not { Result: null } link)
            return Results.NotFound();

        var userId = await currentUser.GetRequiredUserIdAsync();
        var account = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var tokens = AuthEndpoints.IssueTokens(account, keys);
        link.Result = new LinkResultDto(tokens.Token, tokens.RefreshToken, dto.PublicKey, dto.EncryptedKey);
        return Results.NoContent();
    }

    /// <summary>202 while the user hasn't entered the code, the tokens and key once they have (only once).</summary>
    public static IResult Poll([FromRoute] string code, [FromBody] PollLinkDto dto, IMemoryCache cache)
    {
        var key = CacheKey(DeviceLink.Normalize(code));
        if (cache.Get<PendingLink>(key) is not { } link || dto.PollToken is null
            || !CryptographicOperations.FixedTimeEquals(link.PollTokenHash, Hash(dto.PollToken)))
            return Results.NotFound();
        if (link.Result is not { } result)
            return Results.Accepted();

        cache.Remove(key);
        return Results.Ok(result);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
