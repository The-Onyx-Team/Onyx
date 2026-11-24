namespace Onyx.Data.ApiSchema;

/// <param name="TwoFactorCode">Code from the authenticator app, or a recovery code, for accounts with two-factor
/// authentication. Without it such a login answers 428 (<see cref="TwoFactor.RequiredStatus"/>).</param>
public record LoginDto(string Email, string Password, string? TwoFactorCode = null);
public record LoginResultDto(string Token, string RefreshToken);
/// <param name="AcceptPrivacyPolicy">The user ticked that they agree to the privacy policy; required.</param>
/// <param name="PrivacyPolicyVersion">Version of the policy they saw.</param>
public record RegisterDto(string Name, string Email, string Password, bool AcceptPrivacyPolicy = false,
    string? PrivacyPolicyVersion = null);
public record RegisterResultDto(bool Success, string? Message);
public record UpdateProfileDto(string UserName, string? PhoneNumber);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ChangeEmailDto(string NewEmail);
public record RefreshTokenDto(string Token);
public record ForgotPasswordDto(string Email);

/// <param name="AvatarDataUrl">The profile picture as a <c>data:</c> URL, null when none is set.</param>
public record ProfileDto(
    string? UserName,
    string? Email,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    string? AvatarDataUrl,
    DateTime? PrivacyConsentAt = null,
    string? PrivacyPolicyVersion = null,
    bool HasPassword = true);

/// <param name="ContentType"><c>image/png</c>, <c>image/jpeg</c> or <c>image/webp</c>.</param>
public record AvatarDto(string ContentType, byte[] Data);

/// <summary>Public facts about a server, shown before sign-in (privacy policy, registration).</summary>
/// <param name="Operator">Who runs the server, e.g. a name and postal address; null when not configured.</param>
/// <param name="ContactEmail">Address for privacy questions and data requests; null when not configured.</param>
/// <param name="HostingLocation">Where the server runs, e.g. "Germany"; null when not configured.</param>
/// <param name="EmailProvider">Service that delivers the server's emails, e.g. "SMTP2GO"; null when not configured.</param>
/// <param name="SendsEmail">Whether the server sends emails at all (confirmation, password reset).</param>
/// <param name="Version">The server's Onyx version, e.g. <c>0.0+3f2a1b9</c>.</param>
public record ServerInfoDto(string? Operator, string? ContactEmail, bool AllowRegistration,
    string? HostingLocation = null, string? EmailProvider = null, bool SendsEmail = false, string? Version = null);

public record ConfirmEmailDto(string UserId, string Code);
public record ResetPasswordDto(string UserId, string Code, string Password);

/// <summary>An external login provider offered by the server (Google, Microsoft, GitHub).</summary>
public record ExternalSchemeDto(string Name, string DisplayName);

/// <summary>The one-time code the server hands the browser after an external sign-in.</summary>
/// <param name="Verifier">For the apps: the secret whose SHA-256 the app sent when it started the sign-in, so only
/// the app that started it can use the code (like OAuth PKCE).</param>
public record ExternalExchangeDto(string Code, string? TwoFactorCode = null, string? Verifier = null);

/// <summary>An external sign-in that has no account yet.</summary>
public record ExternalPendingDto(string Provider, string? Email);

/// <param name="Ticket">For the apps, which have no browser cookie: the ticket from the sign-in callback.</param>
/// <param name="Verifier">For the apps: see <see cref="ExternalExchangeDto.Verifier"/>.</param>
public record ExternalRegisterDto(bool AcceptPrivacyPolicy, string? PrivacyPolicyVersion, string? Ticket = null,
    string? Verifier = null);

/// <summary>External sign-in from the native apps, which open the system browser and get the result back through
/// <see cref="CallbackUrl"/>.</summary>
public static class AppExternalLogin
{
    /// <summary>Where the server sends the browser at the end; the app is registered for it.</summary>
    public const string CallbackUrl = "onyx://external";

    /// <summary>Starts the sign-in: <c>?provider=Google&amp;challenge=base64url(SHA-256(verifier))</c>.</summary>
    public const string StartPath = "/account/app-external-login";

    public static string Challenge(string verifier) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public record ConsentDto(string PrivacyPolicyVersion);

public record ExternalLoginDto(string Provider, string DisplayName);

/// <summary>Short-lived ticket that lets the browser link an external login to the signed-in account.</summary>
public record LinkTicketDto(string Ticket);

/// <summary>Two-factor authentication with an authenticator app (TOTP).</summary>
public static class TwoFactor
{
    /// <summary>A login of an account with two-factor authentication needs a code (428 Precondition Required).</summary>
    public const int RequiredStatus = 428;
}

/// <param name="RecoveryCodesLeft">Unused recovery codes; 0 when two-factor authentication is off.</param>
public record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft);

/// <param name="Key">The shared secret in groups of four, for typing into the authenticator app.</param>
/// <param name="Uri">The <c>otpauth://</c> address the QR code holds.</param>
public record TwoFactorKeyDto(string Key, string Uri);

/// <param name="Code">A code from the authenticator app (or, to turn it off, a recovery code).</param>
public record TwoFactorCodeDto(string Code);

public record RecoveryCodesDto(IReadOnlyList<string> Codes);
