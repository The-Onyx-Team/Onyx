using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class MauiAuthenticationStateProvider(AuthenticationService authService) : AuthenticationStateProvider
{
    private readonly ClaimsPrincipal m_AnonymousUser = new(new ClaimsIdentity());
    
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var authState = new AuthenticationState(m_AnonymousUser);
        
        var user = await authService.GetUserSessionAsync();

        if (user is null) return authState;
        
        var identity = GetUserClaimsIdentity(user);
            
        authState = new AuthenticationState(new ClaimsPrincipal(identity));

        return authState;
    }

    public async Task UpdateAuthenticationStateAsync(UserSession? session, bool rememberMe = true)
    {
        var cp = m_AnonymousUser;
        
        if (session is not null)
        {
            var identity = GetUserClaimsIdentity(session);
            
            if (rememberMe)
                await authService.SetUserSessionAsync(session);

            cp = new ClaimsPrincipal(identity);
        }
        else
        {
            authService.ClearUserSession();
        }
        
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(cp)));
    }

    private static ClaimsIdentity GetUserClaimsIdentity(UserSession session)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, session.Id ?? string.Empty),
            new Claim(ClaimTypes.Name, session.Name ?? string.Empty),
            new Claim(ClaimTypes.Email, session.Email ?? string.Empty),
            new Claim(ClaimTypes.Role, session.Role ?? string.Empty),
            new Claim("access_token", session.Token),
            new Claim("refresh_token", session.RefreshToken)
        ], "MauiAuth");
        return identity;
    }
}