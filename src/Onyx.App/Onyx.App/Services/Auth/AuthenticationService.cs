using System.Text.Json;
using Onyx.App.Shared.Services.Auth;

namespace Onyx.App.Services.Auth;

public class AuthenticationService
{
    private const string UserSessionKey = "app_user_session";

    public async Task<UserSession?> GetUserSessionAsync()
    {
        UserSession? session = null;

        var userJson = await SecureStorage.Default.GetAsync(UserSessionKey);

        if (!string.IsNullOrWhiteSpace(userJson))
            session = JsonSerializer.Deserialize<UserSession>(userJson);
        
        return session;
    }

    public async Task SetUserSessionAsync(UserSession session)
    {
        var userJson = JsonSerializer.Serialize(session);

        await SecureStorage.Default.SetAsync(UserSessionKey, userJson);
    }
    
    public void ClearUserSession() => SecureStorage.Default.Remove(UserSessionKey);
}