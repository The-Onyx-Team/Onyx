using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.IdentityModel.Tokens;
using Onyx.App.Web.Services.Auth;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Auth;

[TestSubject(typeof(JwtGenerator))]
public class JwtGeneratorTest
{
    private readonly RsaSecurityKey m_TestKey;
    private readonly string m_UserId = Guid.NewGuid().ToString();
    private readonly string m_UserName = "test-user";

    public JwtGeneratorTest()
    {
        var rsa = RSA.Create();
        m_TestKey = new RsaSecurityKey(rsa);
    }

    [Fact]
    public void GenerateToken_ShouldCreateValidToken()
    {
        // Arrange
        var expiration = TimeSpan.FromDays(20);

        // Act
        var token = JwtGenerator.GenerateToken(m_TestKey, m_UserId, m_UserName, expiration);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.ShouldNotBeNull();
        jwtToken.Claims.First(c => c.Type == JwtGenerator.NameIdentifierKey).Value.ShouldBe(m_UserId);
        jwtToken.Claims.First(c => c.Type == JwtGenerator.NameKey).Value.ShouldBe(m_UserName);
        jwtToken.ValidTo.Day.ShouldBe(DateTime.UtcNow.Add(expiration).Date.Day);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldCreateValidRefreshToken()
    {
        // Act
        var token = JwtGenerator.GenerateRefreshToken(m_TestKey, m_UserId, m_UserName);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.ShouldNotBeNull();
        jwtToken.Claims.First(c => c.Type == JwtGenerator.NameIdentifierKey).Value.ShouldBe(m_UserId);
        jwtToken.Claims.First(c => c.Type == JwtGenerator.NameKey).Value.ShouldBe(m_UserName);
        jwtToken.ValidTo.Day.ShouldBe(DateTime.UtcNow.AddMonths(1).Date.Day);
    }
}