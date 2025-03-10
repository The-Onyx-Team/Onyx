using System.Security.Claims;

namespace Onyx.App.Shared.Services.Auth;

public class ExternalLoginData
{
    public required string ProviderDisplayName { get; set; }
    public required string ProviderKey { get; set; }
    public required string LoginProvider { get; set; }
    public required ClaimsPrincipal Principal { get; set; }
}