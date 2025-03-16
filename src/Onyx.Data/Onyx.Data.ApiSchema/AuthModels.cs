namespace Onyx.Data.ApiSchema;

public record LoginDto(string Email, string Password);
public record LoginResultDto(string Token, string RefreshToken);
public record RegisterDto(string Name, string Email, string Password);
public record RegisterResultDto(bool Success, string? Message);
public record UpdateProfileDto(string UserName, string? PhoneNumber);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ChangeEmailDto(string NewEmail);
public record RefreshTokenDto(string Token);
