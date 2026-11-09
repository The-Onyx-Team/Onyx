using System.ComponentModel.DataAnnotations;

namespace Onyx.App.Shared.Services.Auth;

public interface IUserManager
{
    public Task<RegisterResult> RegisterAsync(string name, string email, string password);
    public Task<RegisterResult> ConfirmEmailAsync(string id, string token);
    public Task<LoginResult> LoginAsync(string email, string password, string redirectUri);
    /// <summary>Sends a password reset link. Succeeds whether or not the email is registered, so the result
    /// does not reveal which addresses have accounts.</summary>
    public Task<ResetPasswordResult> ResetPasswordAsync(string email);

    /// <summary>Sets a new password with the reset code from the email.</summary>
    public Task<ResetPasswordResult> SetPasswordAsync(string id, string code, string password);
    public Task<ExternalLoginData> GetExternalLoginDataAsync();
    public Task LogoutAsync();

    /// <summary>Deletes the signed-in user's account with all devices and usage data, then signs out.</summary>
    /// <returns>False if the deletion failed.</returns>
    public Task<bool> DeleteAccountAsync();

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input);
    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input, string? returnUrl);
    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas();
    public Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user);
}

public sealed class EmailInputModel
{
    [Required] [EmailAddress] public string Email { get; set; } = "";
}