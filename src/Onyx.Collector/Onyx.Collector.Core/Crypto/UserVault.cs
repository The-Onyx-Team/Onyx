using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core.Crypto;

/// <summary>Content of an encrypted usage row: one app's time in one hour, split by website.</summary>
public sealed record UsagePayload(
    [property: JsonPropertyName("a")] string AppIdentifier,
    [property: JsonPropertyName("e")] List<UsagePart> Parts);

/// <param name="Domain">Website, for browsers on devices that record websites; null otherwise.</param>
/// <param name="Seconds">Foreground time.</param>
public sealed record UsagePart(
    [property: JsonPropertyName("d")] string? Domain,
    [property: JsonPropertyName("s")] int Seconds);

/// <summary>Content of an encrypted app row: what the devices report, plus the user's own icon.</summary>
public sealed record AppInfoPayload(
    [property: JsonPropertyName("a")] string Identifier,
    [property: JsonPropertyName("n")] string Name,
    [property: JsonPropertyName("c")] string? Category,
    [property: JsonPropertyName("i")] byte[]? Icon,
    [property: JsonPropertyName("ci")] byte[]? CustomIcon = null);

/// <summary>Content of the encrypted profile. The user name and email stay readable for the server: they are how
/// people sign in and find each other.</summary>
/// <param name="DisplayName">The user's real name, shown to friends they share with.</param>
/// <param name="Avatar">Profile picture (JPEG, PNG or WebP, at most 512 KB).</param>
/// <param name="Thumbnail">The picture at 64 × 64 pixels (JPEG), sent to friends with the shared numbers.</param>
public sealed record ProfilePayload(
    [property: JsonPropertyName("n")] string? DisplayName = null,
    [property: JsonPropertyName("p")] string? PhoneNumber = null,
    [property: JsonPropertyName("a")] byte[]? Avatar = null,
    [property: JsonPropertyName("at")] string? AvatarContentType = null,
    [property: JsonPropertyName("t")] byte[]? Thumbnail = null)
{
    public string? AvatarDataUrl => Avatar is { Length: > 0 }
        ? $"data:{AvatarContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(Avatar)}"
        : null;

    public string? ThumbnailDataUrl => Thumbnail is { Length: > 0 }
        ? $"data:image/jpeg;base64,{Convert.ToBase64String(Thumbnail)}"
        : null;
}

/// <summary>A decrypted usage row.</summary>
public sealed record DecryptedUsage(int DeviceId, string Tag, DateTime Hour, UsagePayload Payload);

/// <summary>
/// An unlocked vault: encrypts and decrypts the user's data. Created by <see cref="UnlockAsync"/> (passphrase),
/// <see cref="UnlockWithRecoveryKeyAsync"/> or <see cref="FromMasterKeyAsync"/> (a key kept on the device).
/// </summary>
public sealed class UserVault
{
    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);

    private readonly IOnyxCrypto m_Crypto;
    private readonly ConcurrentDictionary<string, string> m_Tags = new();

    private UserVault(IOnyxCrypto crypto, VaultKeys keys)
    {
        m_Crypto = crypto;
        Keys = keys;
    }

    public VaultKeys Keys { get; }

    public IOnyxCrypto Primitives => m_Crypto;

    public static async Task<UserVault> FromMasterKeyAsync(IOnyxCrypto crypto, byte[] masterKey) =>
        new(crypto, await VaultKeys.FromMasterKeyAsync(crypto, masterKey));

    /// <summary>Creates the key material for a new vault.</summary>
    /// <returns>The vault, what to send to the server, and the recovery key to show the user once.</returns>
    public static async Task<(UserVault Vault, CreateVaultDto Dto, string RecoveryKey)> CreateAsync(IOnyxCrypto crypto,
        string passphrase, VaultSettings settings)
    {
        var masterKey = RandomNumberGenerator.GetBytes(CryptoFormat.KeySize);
        var vault = await FromMasterKeyAsync(crypto, masterKey);
        var (salt, wrapped) = await vault.WrapAsync(passphrase);
        var (privateKey, publicKey) = await crypto.CreateKeyPairAsync();

        var dto = new CreateVaultDto(
            salt,
            CryptoFormat.Iterations,
            wrapped,
            publicKey,
            await crypto.EncryptAsync(vault.Keys.EncryptionKey, privateKey, E2EContext.PrivateKey),
            await vault.EncryptSettingsAsync(settings));
        return (vault, dto, RecoveryKeys.Encode(masterKey));
    }

    /// <exception cref="CryptographicException">Wrong passphrase.</exception>
    public static async Task<UserVault> UnlockAsync(IOnyxCrypto crypto, VaultDto vault, string passphrase)
    {
        var passphraseKey = await crypto.DeriveKeyAsync(passphrase, vault.Salt, vault.Iterations);
        var masterKey = await crypto.DecryptAsync(passphraseKey, vault.WrappedKey, E2EContext.MasterKey);
        return await FromMasterKeyAsync(crypto, masterKey);
    }

    /// <exception cref="CryptographicException">Not the recovery key of this vault.</exception>
    public static async Task<UserVault> UnlockWithRecoveryKeyAsync(IOnyxCrypto crypto, VaultDto vault, string recoveryKey)
    {
        var masterKey = RecoveryKeys.Decode(recoveryKey)
                        ?? throw new CryptographicException("That is not a recovery key");
        var unlocked = await FromMasterKeyAsync(crypto, masterKey);
        // The private key is encrypted with the master key, so decrypting it proves the key is right.
        await unlocked.DecryptPrivateKeyAsync(vault);
        return unlocked;
    }

    /// <summary>Wraps the master key with a (new) passphrase.</summary>
    public async Task<(byte[] Salt, byte[] WrappedKey)> WrapAsync(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(CryptoFormat.SaltSize);
        var passphraseKey = await m_Crypto.DeriveKeyAsync(passphrase, salt, CryptoFormat.Iterations);
        return (salt, await m_Crypto.EncryptAsync(passphraseKey, Keys.MasterKey, E2EContext.MasterKey));
    }

    public async Task<UpdateVaultKeyDto> RewrapAsync(string passphrase)
    {
        var (salt, wrapped) = await WrapAsync(passphrase);
        return new UpdateVaultKeyDto(salt, CryptoFormat.Iterations, wrapped);
    }

    public string RecoveryKey => RecoveryKeys.Encode(Keys.MasterKey);

    public Task<byte[]> DecryptPrivateKeyAsync(VaultDto vault) =>
        m_Crypto.DecryptAsync(Keys.EncryptionKey, vault.EncryptedPrivateKey, E2EContext.PrivateKey);

    /// <summary>The tag of an app: the first 16 bytes of HMAC-SHA256(tag key, identifier), base64url.</summary>
    public async Task<string> TagAsync(string appIdentifier)
    {
        if (m_Tags.TryGetValue(appIdentifier, out var tag))
            return tag;

        var hash = await m_Crypto.HmacSha256Async(Keys.TagKey, Encoding.UTF8.GetBytes("app|" + appIdentifier));
        tag = Base64Url(hash.AsSpan(0, 16));
        m_Tags[appIdentifier] = tag;
        return tag;
    }

    /// <summary>Encrypts collector entries: one row per app and hour, websites inside the row.</summary>
    /// <returns>The rows, and which identifier each tag stands for.</returns>
    public async Task<(List<EncryptedUsageEntryDto> Rows, Dictionary<string, string> Identifiers)> EncryptUsageAsync(
        int deviceId, IEnumerable<UsageEntryDto> entries)
    {
        var groups = entries
            .GroupBy(e => (e.AppIdentifier, Hour: DateTime.SpecifyKind(e.BucketStart, DateTimeKind.Utc)))
            .ToList();

        var identifiers = new Dictionary<string, string>();
        var plain = new List<(string Tag, DateTime Hour, byte[] Plaintext, string Context)>(groups.Count);
        foreach (var group in groups)
        {
            var tag = await TagAsync(group.Key.AppIdentifier);
            identifiers[tag] = group.Key.AppIdentifier;
            var payload = new UsagePayload(group.Key.AppIdentifier, group
                .GroupBy(e => string.IsNullOrEmpty(e.Domain) ? null : e.Domain)
                .Select(g => new UsagePart(g.Key, (int)Math.Round(g.Sum(e => e.ForegroundTime.TotalSeconds))))
                .Where(p => p.Seconds > 0)
                .ToList());
            plain.Add((tag, group.Key.Hour, JsonSerializer.SerializeToUtf8Bytes(payload, s_Json),
                E2EContext.Usage(deviceId, tag, group.Key.Hour)));
        }

        var blobs = await m_Crypto.EncryptManyAsync(Keys.EncryptionKey,
            plain.Select(p => (p.Plaintext, p.Context)).ToList());
        return (plain.Select((p, i) => new EncryptedUsageEntryDto(p.Tag, p.Hour, blobs[i])).ToList(), identifiers);
    }

    /// <summary>Encrypts decrypted rows again, e.g. after removing websites from them.</summary>
    public async Task<List<EncryptedUsageEntryDto>> EncryptRowsAsync(int deviceId, IReadOnlyList<DecryptedUsage> rows)
    {
        var blobs = await m_Crypto.EncryptManyAsync(Keys.EncryptionKey, rows
            .Select(r => (JsonSerializer.SerializeToUtf8Bytes(r.Payload, s_Json), E2EContext.Usage(deviceId, r.Tag, r.Hour)))
            .ToList());
        return rows.Select((r, i) => new EncryptedUsageEntryDto(r.Tag, r.Hour, blobs[i])).ToList();
    }

    /// <summary>Decrypts usage rows. Rows that don't decrypt (tampered with, or from another key) are skipped.</summary>
    public async Task<List<DecryptedUsage>> DecryptUsageAsync(IReadOnlyList<EncryptedUsageDto> rows)
    {
        var plain = await m_Crypto.DecryptManyAsync(Keys.EncryptionKey, rows
            .Select(r => (r.Data, E2EContext.Usage(r.DeviceId, r.Tag, r.Hour)))
            .ToList());

        var result = new List<DecryptedUsage>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            if (plain[i] is not { } bytes || Deserialize<UsagePayload>(bytes) is not { } payload)
                continue;
            result.Add(new DecryptedUsage(rows[i].DeviceId, rows[i].Tag,
                DateTime.SpecifyKind(rows[i].Hour, DateTimeKind.Utc), payload));
        }

        return result;
    }

    public async Task<EncryptedAppDto> EncryptAppAsync(AppInfoPayload app)
    {
        var tag = await TagAsync(app.Identifier);
        return new EncryptedAppDto(tag, await m_Crypto.EncryptAsync(Keys.EncryptionKey,
            JsonSerializer.SerializeToUtf8Bytes(app, s_Json), E2EContext.App(tag)));
    }

    /// <returns>Apps by tag; apps that don't decrypt are skipped.</returns>
    public async Task<Dictionary<string, AppInfoPayload>> DecryptAppsAsync(IReadOnlyList<EncryptedAppDto> apps)
    {
        var plain = await m_Crypto.DecryptManyAsync(Keys.EncryptionKey,
            apps.Select(a => (a.Data, E2EContext.App(a.Tag))).ToList());
        var result = new Dictionary<string, AppInfoPayload>();
        for (var i = 0; i < apps.Count; i++)
            if (plain[i] is { } bytes && Deserialize<AppInfoPayload>(bytes) is { } app)
                result[apps[i].Tag] = app;
        return result;
    }

    public async Task<byte[]> EncryptSettingsAsync(VaultSettings settings) =>
        await m_Crypto.EncryptAsync(Keys.EncryptionKey, JsonSerializer.SerializeToUtf8Bytes(settings, s_Json),
            E2EContext.Settings);

    public async Task<VaultSettings> DecryptSettingsAsync(byte[] blob) =>
        Deserialize<VaultSettings>(await m_Crypto.DecryptAsync(Keys.EncryptionKey, blob, E2EContext.Settings))
        ?? VaultSettings.CreateDefault();

    public async Task<byte[]> EncryptProfileAsync(ProfilePayload profile) =>
        await m_Crypto.EncryptAsync(Keys.EncryptionKey, JsonSerializer.SerializeToUtf8Bytes(profile, s_Json),
            E2EContext.Profile);

    public async Task<ProfilePayload> DecryptProfileAsync(byte[] blob) =>
        Deserialize<ProfilePayload>(await m_Crypto.DecryptAsync(Keys.EncryptionKey, blob, E2EContext.Profile))
        ?? new ProfilePayload();

    /// <summary>Encrypts something for another user (friends' shared numbers).</summary>
    public async Task<byte[]> EncryptForAsync<T>(T value, byte[] privateKey, byte[] publicKey, byte[] recipientPublicKey,
        string context)
    {
        var key = await m_Crypto.SharedKeyAsync(privateKey, publicKey, recipientPublicKey);
        return await m_Crypto.EncryptAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, s_Json), context);
    }

    /// <returns>Null if the data can't be decrypted (e.g. the sender changed keys).</returns>
    public async Task<T?> DecryptFromAsync<T>(byte[] blob, byte[] privateKey, byte[] publicKey, byte[] senderPublicKey,
        string context) where T : class
    {
        try
        {
            var key = await m_Crypto.SharedKeyAsync(privateKey, publicKey, senderPublicKey);
            return Deserialize<T>(await m_Crypto.DecryptAsync(key, blob, context));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>A short code both friends can compare to check that the server did not swap keys.</summary>
    public static string SafetyCode(byte[] publicKeyA, byte[] publicKeyB)
    {
        // Order-independent, so both sides compute the same code.
        var (first, second) = publicKeyA.AsSpan().SequenceCompareTo(publicKeyB) <= 0
            ? (publicKeyA, publicKeyB)
            : (publicKeyB, publicKeyA);
        var hash = SHA256.HashData([.. first, .. second]);
        var number = BitConverter.ToUInt64(hash, 0) % 1_000_000_000_000UL;
        var digits = number.ToString("D12");
        return $"{digits[..4]} {digits[4..8]} {digits[8..]}";
    }

    private static T? Deserialize<T>(byte[] bytes) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, s_Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
