using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class UserSession : User
{
    public required string Token { get; set; }
    public required string RefreshToken { get; set; }
}