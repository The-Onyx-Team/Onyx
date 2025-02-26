using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Onyx.App.Web.Services.Auth;

public class JwtGenerator
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
}