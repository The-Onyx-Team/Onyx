namespace Onyx.App.Services.Auth;

/// <summary>The stored sign-in (secure storage, <see cref="AuthenticationService"/>).</summary>
public class UserSession
{
    public required string Token { get; set; }
    public required string RefreshToken { get; set; }
}
