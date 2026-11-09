using Onyx.App.Services.Auth;
using Onyx.Collector.Core;

namespace Onyx.App.Services.Collection;

/// <summary>
/// Lets <see cref="OnyxApiClient"/> use the session the app's login stores in secure storage, so the UI and the
/// background collector share one set of tokens. The device id is kept in preferences.
/// </summary>
public class MauiCredentialStore(AuthenticationService authenticationService) : ICredentialStore
{
    private const string DeviceIdKey = "onyx_device_id";

    public async Task<CollectorCredentials?> LoadAsync()
    {
        var session = await authenticationService.GetUserSessionAsync();
        if (session is null)
            return null;

        var deviceId = Preferences.Default.Get(DeviceIdKey, -1);
        return new CollectorCredentials(session.Token, session.RefreshToken, deviceId < 0 ? null : deviceId);
    }

    public async Task SaveAsync(CollectorCredentials credentials)
    {
        var session = await authenticationService.GetUserSessionAsync();
        if (session is not null)
        {
            session.Token = credentials.AccessToken;
            session.RefreshToken = credentials.RefreshToken;
            await authenticationService.SetUserSessionAsync(session);
        }

        if (credentials.DeviceId is { } id)
            Preferences.Default.Set(DeviceIdKey, id);
        else
            Preferences.Default.Remove(DeviceIdKey);
    }

    public Task ClearAsync()
    {
        authenticationService.ClearUserSession();
        return Task.CompletedTask;
    }

    /// <summary>Forgets the device id, e.g. when another account signs in on this device.</summary>
    public static void ForgetDevice() => Preferences.Default.Remove(DeviceIdKey);
}
