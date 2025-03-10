using System.ComponentModel.DataAnnotations;

namespace Onyx.App.Shared.Services.Auth;

public interface IUserManager
{
    public Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri);
    public Task<LoginResult> LoginAsync(string email, string password, string redirectUri);
    public Task<ExternalLoginData> GetExternalLoginDataAsync();

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input);
    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input, string? returnUrl);
    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas();
}

public sealed class EmailInputModel
{
    [Required] [EmailAddress] public string Email { get; set; } = "";
}