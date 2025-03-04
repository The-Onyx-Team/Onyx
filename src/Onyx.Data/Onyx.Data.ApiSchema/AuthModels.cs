namespace Onyx.Data.ApiSchema;

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string ConfirmPassword);
public record UpdateProfileRequest(string UserName, string? PhoneNumber);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ChangeEmailRequest(string NewEmail);
public record RefreshTokenRequest(string Token);
