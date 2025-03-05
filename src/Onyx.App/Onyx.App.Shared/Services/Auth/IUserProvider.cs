namespace Onyx.App.Shared.Services.Auth;

public interface IUserProvider
{
    /// <returns>A user object representing the currently signed-in user. Fails if no user is found.</returns>
    public Task<User> GetRequiredUserAsync();
    public Task<string> GetAuthenticatorKeyAsync();
}