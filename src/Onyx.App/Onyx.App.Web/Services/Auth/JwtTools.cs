using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

public class JwtTools
{
    public const string NameIdentifierKey = "nameid";
    public const string NameKey = "unique_name";

    /// <summary>Claim that says what a token may be used for, so a refresh token can't call the API and vice versa.</summary>
    public const string TokenUseKey = "token_use";
    public const string AccessTokenUse = "access";
    public const string RefreshTokenUse = "refresh";

    /// <summary>Copy of the user's security stamp. Changing the password or signing out everywhere rotates the
    /// stamp, which invalidates every refresh token issued before.</summary>
    public const string SecurityStampKey = "sstamp";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromDays(2);

    public static string GenerateToken(RsaSecurityKey key, string userId, string userName, TimeSpan expiration) =>
        CreateToken(key, userId, userName, AccessTokenUse, null, DateTime.UtcNow.Add(expiration));

    public static string GenerateRefreshToken(RsaSecurityKey key, string userId, string userName,
        string? securityStamp = null) =>
        CreateToken(key, userId, userName, RefreshTokenUse, securityStamp, DateTime.UtcNow.AddMonths(1));

    private static string CreateToken(RsaSecurityKey key, string userId, string userName, string tokenUse,
        string? securityStamp, DateTime expires)
    {
        var handler = new JwtSecurityTokenHandler();

        var claims = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
            new Claim(TokenUseKey, tokenUse)
        ]);

        if (securityStamp is not null)
            claims.AddClaim(new Claim(SecurityStampKey, securityStamp));

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });

        return handler.WriteToken(token);
    }

    /// <summary>
    /// Validation rules shared by the API's bearer scheme and the refresh endpoint: the token must be signed
    /// with the application key and must not be expired.
    /// </summary>
    public static TokenValidationParameters CreateValidationParameters(SecurityKey key) => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// Validates a refresh token (signature, lifetime, token use, security stamp) and returns its user.
    /// </summary>
    /// <exception cref="ArgumentException">The token is not a JWT at all.</exception>
    public static async Task<(ApplicationUser? User, string? Error)> ValidateAndGetUserFromToken(
        string token,
        UserManager<ApplicationUser> userManager,
        SecurityKey key)
    {
        if (string.IsNullOrEmpty(token))
            return (null, "No token provided");

        var handler = new JsonWebTokenHandler();
        if (!handler.CanReadToken(token))
            throw new ArgumentException("Token is not a valid JWT", nameof(token));

        var result = await handler.ValidateTokenAsync(token, CreateValidationParameters(key));
        if (!result.IsValid)
            return (null, result.Exception?.Message ?? "Invalid token");

        var claims = result.ClaimsIdentity;

        if (claims.FindFirst(TokenUseKey)?.Value != RefreshTokenUse)
            return (null, "Not a refresh token");

        var userId = claims.FindFirst(NameIdentifierKey)?.Value ?? claims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return (null, "Invalid token claims");

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return (null, "User not found");

        if (user.SecurityStamp is not null && claims.FindFirst(SecurityStampKey)?.Value != user.SecurityStamp)
            return (null, "Token has been revoked");

        return (user, null);
    }
}
