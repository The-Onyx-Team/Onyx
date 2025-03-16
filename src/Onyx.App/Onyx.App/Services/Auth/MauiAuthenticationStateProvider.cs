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

    public async Task UpdateAuthenticationStateAsync(User? user, bool rememberMe = true)
    {
        var cp = m_AnonymousUser;
        
        if (user is not null)
        {
            var identity = GetUserClaimsIdentity(user);
            
            if (rememberMe)
                await authService.SetUserSessionAsync(user);

            cp = new ClaimsPrincipal(identity);
        }
        else
        {
            authService.ClearUserSession();
        }
        
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(cp)));
    }

    private static ClaimsIdentity GetUserClaimsIdentity(User user)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Name, user.Name ?? string.Empty),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.Role, user.Role ?? string.Empty)
        ], "MauiAuth");
        return identity;
    }
}