using Onyx.App.Services.Auth;
using Onyx.App.Shared.Services;
using Onyx.Collector.Core;

namespace Onyx.App.Services.Collection;

/// <summary>
/// Tokens and the encryption key in secure storage, the device id in preferences. The UI and the background
/// collector share them: the collector needs the key to encrypt what it uploads.
/// </summary>
public class MauiCredentialStore(AuthenticationService authenticationService) : ICredentialStore, IVaultKeyOptions
{
    private const string DeviceIdKey = "onyx_device_id";
    private const string VaultKeyKey = "onyx_vault_key";

    public async Task<CollectorCredentials?> LoadAsync()
    {
        var session = await authenticationService.GetUserSessionAsync();
        if (session is null)
            return null;

        var deviceId = Preferences.Default.Get(DeviceIdKey, -1);
        return new CollectorCredentials(session.Token, session.RefreshToken, deviceId < 0 ? null : deviceId,
            await SecureStorage.Default.GetAsync(VaultKeyKey));
    }

    public async Task SaveAsync(CollectorCredentials credentials)
    {
        await authenticationService.SetUserSessionAsync(new UserSession
        {
            Token = credentials.AccessToken,
            RefreshToken = credentials.RefreshToken
        });

        if (credentials.DeviceId is { } id)
            Preferences.Default.Set(DeviceIdKey, id);
        else
            Preferences.Default.Remove(DeviceIdKey);

        if (credentials.VaultKey is { } key)
            await SecureStorage.Default.SetAsync(VaultKeyKey, key);
        else
            SecureStorage.Default.Remove(VaultKeyKey);
    }

    /// <summary>Signing out forgets tokens and key; the device id stays for the next sign-in of the same account.</summary>
    public Task ClearAsync()
    {
        authenticationService.ClearUserSession();
        SecureStorage.Default.Remove(VaultKeyKey);
        return Task.CompletedTask;
    }

    public Task<int?> LoadDeviceIdAsync()
    {
        var id = Preferences.Default.Get(DeviceIdKey, -1);
        return Task.FromResult<int?>(id < 0 ? null : id);
    }

    /// <summary>Forgets the device id, e.g. when the app switches to another server.</summary>
    public static void ForgetDevice() => Preferences.Default.Remove(DeviceIdKey);

    // The background upload needs the key, so the apps always keep it.
    public bool CanChoose => false;

    public bool Remember
    {
        get => true;
        set { }
    }
}
