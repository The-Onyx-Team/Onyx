using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class UserManager(AuthenticationStateProvider authenticationStateProvider) : IUserManager
{
    public async Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri)
    {
        await (authenticationStateProvider as MauiAuthenticationStateProvider)!.UpdateAuthenticationStateAsync(null);

        return new RegisterResult
        {
            Success = true,
        };
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string redirectUri)
    {
        await (authenticationStateProvider as MauiAuthenticationStateProvider)!.UpdateAuthenticationStateAsync(null);

        return new LoginResult
        {
            Success = true,
        };
    }

    public Task<ExternalLoginData> GetExternalLoginDataAsync()
    {
        throw new NotImplementedException();
    }

    public Task LogoutAsync()
    {
        return (authenticationStateProvider as MauiAuthenticationStateProvider)!.UpdateAuthenticationStateAsync(null);
    }

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input)
    {
        throw new NotImplementedException();
    }

    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input, string? returnUrl)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas()
    {
        return Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
    }

    public Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user)
    {
        return Task.FromResult<IEnumerable<ExternalLoginData>>([])!;
    }
}