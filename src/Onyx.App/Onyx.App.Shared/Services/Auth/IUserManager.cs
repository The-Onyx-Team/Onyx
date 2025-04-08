using System.ComponentModel.DataAnnotations;

namespace Onyx.App.Shared.Services.Auth;

public interface IUserManager
{
    public Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri);
    public Task<LoginResult> LoginAsync(string email, string password, string redirectUri);
    public Task<ExternalLoginData> GetExternalLoginDataAsync();
    public Task LogoutAsync();

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input);
    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input, string? returnUrl);
    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas();
    public Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user);
    public Task<bool> RemoveLogin(User user, string loginProvider, string providerKey);
    public Task<bool> ChangeEmail(User user, string newEmail);
    public Task<bool> ChangePhoneNumber(User user, string phoneNumber);
    public Task<bool> ChangePassword(User user, string oldPassword, string newPassword);
    public Task<bool> SendChangePasswordEmail(string id,string email);
    public Task<bool> ChangeUserName(User user, string userName);
    public Task<string[]> GetRecoveryCodes(User user);
}

public sealed class EmailInputModel
{
    [Required] [EmailAddress] public string Email { get; set; } = "";
}