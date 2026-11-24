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
    IPlatformService platform,
    IEnumerable<IExternalLoginLauncher> launchers) : IUserManager
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

    public async Task<LoginResult> LoginAsync(string email, string password, string? twoFactorCode = null)
    {
        try
        {
            var tokens = await api.SendAnonymousAsync<LoginResultDto>(HttpMethod.Post, "/api/auth/login",
                new LoginDto(email, password, twoFactorCode));
            await SignedInAsync(tokens);
            return new LoginResult { Success = true };
        }
        catch (HttpRequestException e) when ((int?)e.StatusCode == TwoFactor.RequiredStatus)
        {
            return new LoginResult { Success = false, RequiresTwoFactor = true, Message = OnyxApiClient.ErrorMessage(e) };
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return new LoginResult
            {
                Success = false,
                // A wrong two-factor code says so; anything else stays vague about which part was wrong.
                RequiresTwoFactor = twoFactorCode is not null,
                Message = twoFactorCode is not null ? OnyxApiClient.ErrorMessage(e) : "Invalid email or password"
            };
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
        // The apps need a launcher for the system browser (not every platform has one).
        if (platform.IsMaui() && !launchers.Any())
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

    public async Task<LoginResult> CompleteExternalLoginAsync(string code, string? twoFactorCode = null,
        string? verifier = null)
    {
        try
        {
            await SignedInAsync(await api.SendAnonymousAsync<LoginResultDto>(HttpMethod.Post,
                "/api/auth/external/exchange", new ExternalExchangeDto(code, twoFactorCode, verifier)));
            return new LoginResult { Success = true };
        }
        catch (HttpRequestException e)
        {
            return new LoginResult
            {
                Success = false,
                RequiresTwoFactor = (int?)e.StatusCode == TwoFactor.RequiredStatus
                                    || (twoFactorCode is not null && e.StatusCode == HttpStatusCode.BadRequest),
                Message = OnyxApiClient.ErrorMessage(e)
            };
        }
    }

    public async Task<ExternalPendingDto?> GetPendingExternalLoginAsync(string? ticket = null)
    {
        try
        {
            return await api.SendAnonymousAsync<ExternalPendingDto>(HttpMethod.Get, ticket is null
                ? "/api/auth/external/pending"
                : $"/api/auth/external/pending?ticket={Uri.EscapeDataString(ticket)}");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<LoginResult> RegisterExternalAsync(bool acceptPrivacyPolicy, string? ticket = null,
        string? verifier = null)
    {
        try
        {
            await SignedInAsync(await api.SendAnonymousAsync<LoginResultDto>(HttpMethod.Post,
                "/api/auth/external/register",
                new ExternalRegisterDto(acceptPrivacyPolicy, PrivacyPolicy.Version, ticket, verifier)));
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
