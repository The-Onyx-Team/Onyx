namespace Onyx.App.Shared.Services.Auth;

public class User
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool Has2Fa { get; set; }
}