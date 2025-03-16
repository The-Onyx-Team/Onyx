using System.Text.Json;
using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class AuthenticationService
{
    private const string UserSessionKey = "app_user_session";

    public async Task<User?> GetUserSessionAsync()
    {
        User? user = null;

        var userJson = await SecureStorage.Default.GetAsync(UserSessionKey);

        if (!string.IsNullOrWhiteSpace(userJson))
            user = JsonSerializer.Deserialize<User>(userJson);
        
        return user;
    }

    public async Task SetUserSessionAsync(User user)
    {
        var userJson = JsonSerializer.Serialize(user);

        await SecureStorage.Default.SetAsync(UserSessionKey, userJson);
    }
    
    public void ClearUserSession() => SecureStorage.Remove(UserSessionKey);
}