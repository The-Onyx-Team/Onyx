using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Client;

public enum VaultState
{
    SignedOut,

    /// <summary>The account has not set up encryption yet.</summary>
    NoVault,

    /// <summary>Set up, but the key is not on this device: the passphrase is needed.</summary>
    Locked,

    Unlocked,

    /// <summary>The server could not be reached to find out.</summary>
    Offline
}

/// <summary>
/// The user's end-to-end encryption on this device: setting it up, unlocking with the passphrase or recovery
/// key, and the encrypted settings document (with retries when another device changed it at the same time).
/// </summary>
public sealed class VaultService(
    OnyxApiClient api,
    ICredentialStore store,
    IStorage storage,
    IVaultKeyOptions keyOptions,
    ILogger<VaultService> logger)
{
    private const string LastUserIdKey = "onyx_last_user_id";

    private VaultDto? m_Info;
    private VaultSettings? m_Settings;
    private int m_SettingsVersion;
    private (byte[] Private, byte[] Public)? m_KeyPair;

    /// <summary>Raised when the vault was unlocked or locked, or the settings changed.</summary>
    public event Action? Changed;

    public async Task<VaultState> GetStateAsync(bool refresh = false)
    {
        if (await store.LoadAsync() is null)
            return VaultState.SignedOut;

        try
        {
            if (refresh || m_Info is null)
                m_Info = await api.GetVaultInfoAsync();
        }
        catch (NotSignedInException)
        {
            return VaultState.SignedOut;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Could not load the vault: {Message}", e.Message);
            return await api.GetVaultAsync() is null ? VaultState.Offline : VaultState.Unlocked;
        }

        if (m_Info is null)
            return VaultState.NoVault;
        return await api.GetVaultAsync() is null ? VaultState.Locked : VaultState.Unlocked;
    }

    /// <summary>Sets up encryption for the account.</summary>
    /// <returns>The recovery key, to show the user once.</returns>
    public async Task<string> SetupAsync(string passphrase)
    {
        var (vault, dto, recoveryKey) = await UserVault.CreateAsync(api.Crypto, passphrase, VaultSettings.CreateDefault());
        await api.CreateVaultAsync(dto);
        await KeepKeyAsync(vault.Keys.MasterKey);
        m_Info = null;
        m_Settings = null;
        // No Changed event: the setup screen stays until the user confirms they saved the recovery key.
        return recoveryKey;
    }

    /// <returns>False if the passphrase is wrong.</returns>
    public async Task<bool> UnlockAsync(string passphrase)
    {
        var info = await RequireInfoAsync();
        try
        {
            var vault = await UserVault.UnlockAsync(api.Crypto, info, passphrase);
            await KeepKeyAsync(vault.Keys.MasterKey);
            Changed?.Invoke();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Unlocks with the recovery key and sets a new passphrase (the old one was forgotten).</summary>
    /// <returns>False if it is not the recovery key of this account.</returns>
    public async Task<bool> RecoverAsync(string recoveryKey, string newPassphrase)
    {
        var info = await RequireInfoAsync();
        UserVault vault;
        try
        {
            vault = await UserVault.UnlockWithRecoveryKeyAsync(api.Crypto, info, recoveryKey);
        }
        catch (CryptographicException)
        {
            return false;
        }

        await api.UpdateVaultKeyAsync(await vault.RewrapAsync(newPassphrase));
        await KeepKeyAsync(vault.Keys.MasterKey);
        m_Info = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Checks the passphrase without changing anything (before showing the recovery key, say).</summary>
    public async Task<bool> VerifyPassphraseAsync(string passphrase)
    {
        try
        {
            await UserVault.UnlockAsync(api.Crypto, await RequireInfoAsync(refresh: true), passphrase);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Sets a new passphrase. Other devices keep working: they keep the key, not the passphrase.</summary>
    public async Task ChangePassphraseAsync(string newPassphrase)
    {
        var vault = await GetVaultAsync();
        await api.UpdateVaultKeyAsync(await vault.RewrapAsync(newPassphrase));
        m_Info = null;
    }

    /// <summary>Removes the key from this device; the passphrase is needed again.</summary>
    public async Task LockAsync()
    {
        await api.StoreVaultKeyAsync(null);
        Forget();
        Changed?.Invoke();
    }

    /// <summary>Drops what is cached in memory (after signing out or in).</summary>
    public void Forget()
    {
        m_Info = null;
        m_Settings = null;
        m_KeyPair = null;
    }

    /// <exception cref="VaultLockedException">The key is not on this device.</exception>
    public async Task<UserVault> GetVaultAsync() =>
        await api.GetVaultAsync() ?? throw new VaultLockedException("Enter your passphrase to unlock your data.");

    public async Task<string> GetRecoveryKeyAsync() => (await GetVaultAsync()).RecoveryKey;

    public async Task<VaultSettings> GetSettingsAsync(bool refresh = false)
    {
        if (m_Settings is not null && !refresh)
            return m_Settings;

        var info = await RequireInfoAsync(refresh: true);
        var vault = await GetVaultAsync();
        m_Settings = await vault.DecryptSettingsAsync(info.Settings);
        m_SettingsVersion = info.SettingsVersion;
        return m_Settings;
    }

    /// <summary>Changes the settings and stores them. If another device changed them in the meantime, the change
    /// is applied again to their version.</summary>
    public async Task<VaultSettings> UpdateSettingsAsync(Action<VaultSettings> change)
    {
        var vault = await GetVaultAsync();
        for (var attempt = 0; ; attempt++)
        {
            var settings = await GetSettingsAsync(refresh: attempt > 0);
            change(settings);
            try
            {
                m_SettingsVersion = await api.UpdateVaultSettingsAsync(await vault.EncryptSettingsAsync(settings),
                    m_SettingsVersion);
                m_Settings = settings;
                Changed?.Invoke();
                return settings;
            }
            catch (VaultConflictException) when (attempt < 3)
            {
                logger.LogInformation("Settings changed elsewhere, applying the change again");
            }
        }
    }

    /// <summary>The key pair for sharing with friends.</summary>
    public async Task<(byte[] Private, byte[] Public)> GetKeyPairAsync()
    {
        if (m_KeyPair is { } pair)
            return pair;
        var info = await RequireInfoAsync();
        var vault = await GetVaultAsync();
        m_KeyPair = (await vault.DecryptPrivateKeyAsync(info), info.PublicKey);
        return m_KeyPair.Value;
    }

    public async Task<DateTime?> GetCreatedAtAsync() => (await RequireInfoAsync()).CreatedAt;

    public async Task<string?> LastUserIdAsync() =>
        await storage.ContainKeyAsync(LastUserIdKey) ? await storage.GetItemAsync<string>(LastUserIdKey) : null;

    public async Task SetLastUserIdAsync(string? userId)
    {
        if (userId is not null)
            await storage.SetItemAsync(LastUserIdKey, userId);
    }

    public bool CanChooseRemember => keyOptions.CanChoose;

    public bool Remember
    {
        get => keyOptions.Remember;
        set => keyOptions.Remember = value;
    }

    private async Task KeepKeyAsync(byte[] masterKey) => await api.StoreVaultKeyAsync(masterKey);

    private async Task<VaultDto> RequireInfoAsync(bool refresh = false)
    {
        if (refresh || m_Info is null)
            m_Info = await api.GetVaultInfoAsync();
        return m_Info ?? throw new InvalidOperationException("Encryption is not set up for this account.");
    }
}
