namespace Onyx.Data.ApiSchema;

public record LoginDto(string Email, string Password);
public record LoginResultDto(string Token, string RefreshToken);
public record RegisterDto(string Name, string Email, string Password);
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
    string? AvatarDataUrl);

/// <param name="ContentType"><c>image/png</c>, <c>image/jpeg</c> or <c>image/webp</c>.</param>
public record AvatarDto(string ContentType, byte[] Data);

/// <summary>Public facts about a server, shown before sign-in (privacy policy, registration).</summary>
/// <param name="Operator">Who runs the server, e.g. a name and postal address; null when not configured.</param>
/// <param name="ContactEmail">Address for privacy questions and data requests; null when not configured.</param>
public record ServerInfoDto(string? Operator, string? ContactEmail, bool AllowRegistration);
