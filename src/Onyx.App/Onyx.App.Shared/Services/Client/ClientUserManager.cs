using System.Net;
using System.Security.Claims;
using Onyx.App.Shared.Services.Auth;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Client;

/// <summary><see cref="IUserManager"/> over the API, for the web app (WebAssembly) and the native apps.</summary>
public sealed class ClientUserManager(
    OnyxApiClient api,
    ICredentialStore store,
    ClientAuthStateProvider authState,
    VaultService vault,
    IPlatformService platform) : IUserManager
{
    public async Task<RegisterResult> RegisterAsync(string name, string email, string password, bool acceptPrivacyPolicy)
    {
        try
        {
            await api.SendAnonymousAsync<object?>(HttpMethod.Post, "/api/auth/register",
                new RegisterDto(name, email, password, acceptPrivacyPolicy, PrivacyPolicy.Version));
            return new RegisterResult { Success = true };
        }
        catch (HttpRequestException e)
        {
            return new RegisterResult
            {
                Success = false,
                Message = e.StatusCode == HttpStatusCode.Forbidden
                    ? "Registration is disabled on this server."
                    : OnyxApiClient.ErrorMessage(e)
            };
        }
    }

    public async Task<RegisterResult> ConfirmEmailAsync(string id, string token)
    {
        try
        {
            await api.SendAnonymousAsync<object?>(HttpMethod.Post, "/api/auth/confirm-email", new ConfirmEmailDto(id, token));
            return new RegisterResult { Success = true };
        }
        catch (HttpRequestException e)
        {
            return new RegisterResult { Success = false, Message = OnyxApiClient.ErrorMessage(e) };
        }
    }

    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        try
        {
            var tokens = await api.SendAnonymousAsync<LoginResultDto>(HttpMethod.Post, "/api/auth/login",
                new LoginDto(email, password));
            await SignedInAsync(tokens);
            return new LoginResult { Success = true };
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return new LoginResult { Success = false, Message = "Invalid email or password" };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new LoginResult { Success = false, Message = $"The server could not be reached: {e.Message}" };
        }
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(string email)
    {
        try
        {
            await api.SendAnonymousAsync<object?>(HttpMethod.Post, "/api/auth/forgot-password", new ForgotPasswordDto(email));
            return new ResetPasswordResult { Success = true };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new ResetPasswordResult { Success = false };
        }
    }

    public async Task<ResetPasswordResult> SetPasswordAsync(string id, string code, string password)
    {
        try
        {
            await api.SendAnonymousAsync<object?>(HttpMethod.Post, "/api/auth/reset-password",
                new ResetPasswordDto(id, code, password));
            return new ResetPasswordResult { Success = true };
        }
        catch (HttpRequestException e)
        {
            return new ResetPasswordResult { Success = false, Message = OnyxApiClient.ErrorMessage(e) };
        }
    }

    public async Task<string?> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            await api.SendAuthorizedAsync<object?>(HttpMethod.Put, "/api/user/password",
                new ChangePasswordDto(currentPassword, newPassword));
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException)
        {
            return OnyxApiClient.ErrorMessage(e);
        }
    }

    public async Task LogoutAsync()
    {
        vault.Forget();
        await api.LogoutAsync();
        authState.Refresh();
    }

    public async Task<bool> DeleteAccountAsync()
    {
        try
        {
            await api.DeleteAccountAsync();
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException)
        {
            return false;
        }

        vault.Forget();
        await store.ClearAsync();
        authState.Refresh();
        return true;
    }

    public async Task<IReadOnlyList<ExternalSchemeDto>> GetExternalSchemesAsync()
    {
        // External logins are browser redirects to the provider; the apps sign in with a password.
        if (platform.IsMaui())
            return [];
        try
        {
            return await api.SendAnonymousAsync<List<ExternalSchemeDto>>(HttpMethod.Get, "/api/auth/schemes");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    public async Task<LoginResult> CompleteExternalLoginAsync(string code)
    {
        try
        {
            await SignedInAsync(await api.SendAnonymousAsync<LoginResultDto>(HttpMethod.Post,
                "/api/auth/external/exchange", new ExternalExchangeDto(code)));
            return new LoginResult { Success = true };
        }
        catch (HttpRequestException e)
        {
            return new LoginResult { Success = false, Message = OnyxApiClient.ErrorMessage(e) };
        }
    }

    public async Task<ExternalPendingDto?> GetPendingExternalLoginAsync()
    {
        try
        {
            return await api.SendAnonymousAsync<ExternalPendingDto>(HttpMethod.Get, "/api/auth/external/pending");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<LoginResult> RegisterExternalAsync(bool acceptPrivacyPolicy)
    {
        try
        {
            await SignedInAsync(await api.SendAnonymousAsync<LoginResultDto>(HttpMethod.Post,
                "/api/auth/external/register", new ExternalRegisterDto(acceptPrivacyPolicy, PrivacyPolicy.Version)));
            return new LoginResult { Success = true };
        }
        catch (HttpRequestException e)
        {
            return new LoginResult { Success = false, Message = OnyxApiClient.ErrorMessage(e) };
        }
    }

    private async Task SignedInAsync(LoginResultDto tokens)
    {
        // The same account signing in again keeps this device's registration; another account gets a new one.
        var userId = ClientAuthStateProvider.Principal(tokens.Token).FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var previous = await store.LoadDeviceIdAsync();
        var sameUser = userId is not null && userId == await vault.LastUserIdAsync();
        await api.SaveTokensAsync(tokens, sameUser ? previous : null);
        await vault.SetLastUserIdAsync(userId);
        vault.Forget();
        authState.Refresh();
    }
}
