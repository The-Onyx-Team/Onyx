using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Services.Api;
using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class UserManager(AuthenticationStateProvider authenticationStateProvider, AuthApi api) : IUserManager
{
    public async Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri)
    {
        var result = await api.RegisterAsync(name, email, password);

        if (result is null)
            return new RegisterResult
            {
                Success = false,
                Message = "Api error",
            };

        if (result.Success)
            return new RegisterResult
            {
                Success = true,
            };

        return new RegisterResult
        {
            Success = false,
            Message = result.Message,
        };
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string redirectUri)
    {
        var result = await api.LoginAsync(email, password);

        if (result is null)
        {
            await (authenticationStateProvider as MauiAuthenticationStateProvider)!
                .UpdateAuthenticationStateAsync(null);
            return new LoginResult()
            {
                Success = false,
                Message = "Invalid email or password"
            };
        }

        try
        {
            var keyHandler = new JwtSecurityTokenHandler();
            var token = keyHandler.ReadJwtToken(result.Token);
            var userId = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
            var userName = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
            var role = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Role)?.Value;
            var phone = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.MobilePhone)?.Value;
            var amr = token.Claims.FirstOrDefault(x => x.Type == "amr")?.Value;

            await (authenticationStateProvider as MauiAuthenticationStateProvider)!
                .UpdateAuthenticationStateAsync(new UserSession()
                {
                    Token = result.Token,
                    RefreshToken = result.RefreshToken,
                    Email = email,
                    Name = userName,
                    Id = userId,
                    Role = role,
                    PhoneNumber = phone,
                    Has2Fa = amr == "mfa"
                });

            return new LoginResult
            {
                Success = true
            };
        }
        catch (Exception)
        {
            return new LoginResult()
            {
                Success = false,
                Message = "Failed to parse JWT Token"
            };
        }
    }

    public Task LogoutAsync()
    {
        return (authenticationStateProvider as MauiAuthenticationStateProvider)!.UpdateAuthenticationStateAsync(null);
    }

    public Task<ExternalLoginData> GetExternalLoginDataAsync()
    {
        throw new NotImplementedException();
    }

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input)
    {
        throw new NotImplementedException();
    }

    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input,
        string? returnUrl)
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