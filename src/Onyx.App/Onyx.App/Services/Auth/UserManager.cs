using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Services.Api;
using Onyx.App.Services.Collection;
using Onyx.Collector.Core;
using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class UserManager(AuthenticationStateProvider authenticationStateProvider, AuthApi api, OnyxApiClient apiClient)
    : IUserManager
{
    private const string LastUserIdKey = "onyx_last_user_id";

    public async Task<RegisterResult> RegisterAsync(string name, string email, string password)
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

    /// <remarks>Confirmation links from emails open in the browser, on the web app.</remarks>
    public Task<RegisterResult> ConfirmEmailAsync(string id, string token) =>
        Task.FromResult(new RegisterResult { Success = false, Message = "Open the link from the email in your browser." });

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
            // ReadJwtToken does not map claim types, so the short JWT names ("nameid", "unique_name") are used.
            var userId = token.Claims.FirstOrDefault(x => x.Type is "nameid" or ClaimTypes.NameIdentifier)?.Value;
            var userName = token.Claims.FirstOrDefault(x => x.Type is "unique_name" or ClaimTypes.Name)?.Value;

            // A different account must not upload under the previous account's device.
            if (Preferences.Default.Get(LastUserIdKey, (string?)null) != userId)
            {
                MauiCredentialStore.ForgetDevice();
                Preferences.Default.Set(LastUserIdKey, userId);
            }
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

    public async Task<ResetPasswordResult> ResetPasswordAsync(string email) =>
        new() { Success = await api.ForgotPasswordAsync(email) };

    /// <remarks>Reset links from emails open in the browser, on the web app.</remarks>
    public Task<ResetPasswordResult> SetPasswordAsync(string id, string code, string password) =>
        Task.FromResult(new ResetPasswordResult { Success = false });

    public async Task<bool> DeleteAccountAsync()
    {
        try
        {
            await apiClient.DeleteAccountAsync();
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException)
        {
            return false;
        }

        MauiCredentialStore.ForgetDevice();
        await LogoutAsync();
        return true;
    }

    public Task LogoutAsync()
    {
        return (authenticationStateProvider as MauiAuthenticationStateProvider)!.UpdateAuthenticationStateAsync(null);
    }

    // External logins (Google, Microsoft, GitHub) are browser redirects handled by the web app. The app lists no
    // external schemes (GetExternalAuthenticationSchemas), so its pages never call these.

    public Task<ExternalLoginData> GetExternalLoginDataAsync() =>
        throw new NotSupportedException("External logins are only available on the web dashboard");

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input) =>
        throw new NotSupportedException("External logins are only available on the web dashboard");

    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input,
        string? returnUrl) =>
        throw new NotSupportedException("External logins are only available on the web dashboard");

    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas()
    {
        return Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
    }

    public Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user)
    {
        return Task.FromResult<IEnumerable<ExternalLoginData>>([])!;
    }
}