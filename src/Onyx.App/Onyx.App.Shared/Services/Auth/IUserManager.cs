using System.ComponentModel.DataAnnotations;

namespace Onyx.App.Shared.Services.Auth;

public interface IUserManager
{
    public Task<RegisterResult> RegisterAsync(string name, string email, string password);
    public Task<RegisterResult> ConfirmEmailAsync(string id, string token);
    public Task<LoginResult> LoginAsync(string email, string password, string redirectUri);
    public Task<ResetPasswordResult> ResetPasswordAsync(string email);
    public Task<ResetPasswordResult> SetPasswordAsync(string id, string password);
    public Task<ExternalLoginData> GetExternalLoginDataAsync();
    public Task LogoutAsync();

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input);
    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input, string? returnUrl);
    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas();
    public Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user);
}

public sealed class EmailInputModel
{
    [Required] [EmailAddress] public string Email { get; set; } = "";
}