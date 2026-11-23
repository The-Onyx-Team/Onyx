namespace Onyx.App.Shared.Services.Auth;

public interface IUserProvider
{
    /// <returns>The signed-in user. Fails if nobody is signed in.</returns>
    public Task<User> GetRequiredUserAsync();

    /// <summary>The key for authenticator apps, created on first use.</summary>
    public Task<string> GetAuthenticatorKeyAsync();
}
