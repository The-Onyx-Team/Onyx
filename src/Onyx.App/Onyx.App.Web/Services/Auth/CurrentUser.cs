using System.Security.Claims;

namespace Onyx.App.Web.Services.Auth;

/// <summary>The user of the current API request.</summary>
public class CurrentUser(IHttpContextAccessor httpContextAccessor)
{
    public Task<string?> GetUserIdAsync()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return Task.FromResult(principal?.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            : null);
    }

    /// <exception cref="UnauthorizedAccessException">Nobody is signed in.</exception>
    public async Task<string> GetRequiredUserIdAsync() =>
        await GetUserIdAsync() ?? throw new UnauthorizedAccessException("User not found");
}
