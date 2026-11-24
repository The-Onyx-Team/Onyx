using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Auth;

/// <summary>Account actions. The web app and the native apps use the same implementation over the API.</summary>
public interface IUserManager
{
    /// <param name="acceptPrivacyPolicy">The user ticked that they agree to the privacy policy.</param>
    Task<RegisterResult> RegisterAsync(string name, string email, string password, bool acceptPrivacyPolicy);

    Task<RegisterResult> ConfirmEmailAsync(string id, string token);

    /// <param name="twoFactorCode">Authenticator or recovery code, after a result with
    /// <see cref="LoginResult.RequiresTwoFactor"/>.</param>
    Task<LoginResult> LoginAsync(string email, string password, string? twoFactorCode = null);

    /// <summary>Sends a password reset link. Succeeds whether or not the email is registered, so the result
    /// does not reveal which addresses have accounts.</summary>
    Task<ResetPasswordResult> ResetPasswordAsync(string email);

    /// <summary>Sets a new password with the reset code from the email.</summary>
    Task<ResetPasswordResult> SetPasswordAsync(string id, string code, string password);

    /// <returns>Null on success, otherwise a message for the user.</returns>
    Task<string?> ChangePasswordAsync(string currentPassword, string newPassword);

    /// <summary>Signs out on this device and forgets the encryption key here.</summary>
    Task LogoutAsync();

    /// <summary>Deletes the signed-in user's account with all devices and data, then signs out.</summary>
    /// <returns>False if the deletion failed.</returns>
    Task<bool> DeleteAccountAsync();

    /// <summary>External login providers offered by the server (web only; the apps show none).</summary>
    Task<IReadOnlyList<ExternalSchemeDto>> GetExternalSchemesAsync();

    /// <summary>Signs in with the one-time code the server hands out after an external login.</summary>
    /// <param name="verifier">Apps only: the secret the sign-in was started with.</param>
    Task<LoginResult> CompleteExternalLoginAsync(string code, string? twoFactorCode = null, string? verifier = null);

    /// <summary>The external login that has no account yet, or null.</summary>
    /// <param name="ticket">Apps only: the ticket from the sign-in callback (the web app uses a cookie).</param>
    Task<ExternalPendingDto?> GetPendingExternalLoginAsync(string? ticket = null);

    /// <summary>Creates an account for the pending external login and signs in.</summary>
    Task<LoginResult> RegisterExternalAsync(bool acceptPrivacyPolicy, string? ticket = null, string? verifier = null);
}
