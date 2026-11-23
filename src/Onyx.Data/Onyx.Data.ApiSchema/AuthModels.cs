namespace Onyx.Data.ApiSchema;

public record LoginDto(string Email, string Password);
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
public record ExternalExchangeDto(string Code);

/// <summary>An external sign-in that has no account yet.</summary>
public record ExternalPendingDto(string Provider, string? Email);

public record ExternalRegisterDto(bool AcceptPrivacyPolicy, string? PrivacyPolicyVersion);

public record ConsentDto(string PrivacyPolicyVersion);

public record ExternalLoginDto(string Provider, string DisplayName);

/// <summary>Short-lived ticket that lets the browser link an external login to the signed-in account.</summary>
public record LinkTicketDto(string Ticket);
