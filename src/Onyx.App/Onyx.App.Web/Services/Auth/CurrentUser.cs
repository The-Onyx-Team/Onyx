using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Onyx.App.Web.Services.Auth;

/// <summary>
/// Resolves the signed-in user for services that run both behind the REST API and inside Blazor components.
/// </summary>
/// <remarks>
/// API requests carry the user on <see cref="HttpContext"/>. Interactive Blazor Server components run on a
/// SignalR circuit where <see cref="IHttpContextAccessor"/> is not reliable, so the
/// <see cref="AuthenticationStateProvider"/> is asked first. Outside a circuit (plain API request) the provider
/// is never initialized and throws <see cref="InvalidOperationException"/>, which selects the HttpContext path.
/// </remarks>
public class CurrentUser(IHttpContextAccessor httpContextAccessor, IServiceProvider services)
{
    public async Task<string?> GetUserIdAsync()
    {
        var principal = await GetCircuitUserAsync() ?? httpContextAccessor.HttpContext?.User;

        return principal?.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
    }

    /// <exception cref="UnauthorizedAccessException">Nobody is signed in.</exception>
    public async Task<string> GetRequiredUserIdAsync() =>
        await GetUserIdAsync() ?? throw new UnauthorizedAccessException("User not found");

    private async Task<ClaimsPrincipal?> GetCircuitUserAsync()
    {
        if (services.GetService<AuthenticationStateProvider>() is not { } provider)
            return null;

        try
        {
            var state = await provider.GetAuthenticationStateAsync();
            return state.User;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
