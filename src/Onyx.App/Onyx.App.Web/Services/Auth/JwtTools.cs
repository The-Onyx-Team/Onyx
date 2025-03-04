using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

public class JwtTools
{
    public const string NameIdentifierKey = "nameid";
    public const string NameKey = "unique_name";
    
    public static string GenerateToken(RsaSecurityKey key, string userId, string userName, TimeSpan expiration)
    {
        var handler = new JwtSecurityTokenHandler();
        
        var claims = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName)
        ]);
        
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Expires = DateTime.UtcNow.Add(expiration),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });
        
        var tokenString = handler.WriteToken(token);
        
        return tokenString;
    }
    
    public static string GenerateRefreshToken(RsaSecurityKey key, string userId, string userName)
    {
        var handler = new JwtSecurityTokenHandler();
        
        var claims = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName)
        ]);
        
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Expires = DateTime.UtcNow.AddMonths(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });
        
        var tokenString = handler.WriteToken(token);
        
        return tokenString;
    }
    
    public static async Task<(ApplicationUser? User, string? Error)> ValidateAndGetUserFromToken(
        string token,
        UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrEmpty(token))
            return (null, "No token provided");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        var userId = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
        var userName = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userName))
            return (null, "Invalid token claims");

        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.Email != userName)
            return (null, "User not found or invalid");

        return (user, null);
    }
}