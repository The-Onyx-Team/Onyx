namespace Onyx.App.Shared.Services.Auth;

public class LoginResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>The account has two-factor authentication: sign in again with a code.</summary>
    public bool RequiresTwoFactor { get; set; }
}
