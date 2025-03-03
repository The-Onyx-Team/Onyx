using Onyx.Data.ApiSchema.Auth;

namespace Onyx.App.Shared.Services;

public interface IUserManager
{
    public Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri);
    public Task<LoginResult> LoginAsync(string email, string password, string redirectUri);
}