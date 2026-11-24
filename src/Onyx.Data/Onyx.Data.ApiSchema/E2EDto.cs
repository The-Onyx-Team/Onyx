namespace Onyx.Data.ApiSchema;

// End-to-end encryption. The server stores what these DTOs carry but can't read the encrypted parts: usage, app
// names and icons, settings, goals and what friends share are encrypted by the clients with keys the server never
// sees. See docs/Writerside/topics/Encryption.md.

/// <summary>A user's key material as stored on the server.</summary>
/// <param name="Salt">PBKDF2 salt of the passphrase.</param>
/// <param name="Iterations">PBKDF2 iterations.</param>
/// <param name="WrappedKey">The master key, encrypted with the key derived from the passphrase.</param>
/// <param name="PublicKey">ECDH P-256 public key (65 bytes), for sharing with friends.</param>
/// <param name="EncryptedPrivateKey">The matching private key, encrypted with the master key.</param>
/// <param name="Settings">Encrypted settings document (app names, categories, goals, excluded websites, ...).</param>
/// <param name="SettingsVersion">Increases with every settings change; used to detect concurrent edits.</param>
public record VaultDto(
    byte[] Salt,
    int Iterations,
    byte[] WrappedKey,
    byte[] PublicKey,
    byte[] EncryptedPrivateKey,
    byte[] Settings,
    int SettingsVersion,
    DateTime CreatedAt);

public record CreateVaultDto(
    byte[] Salt,
    int Iterations,
    byte[] WrappedKey,
    byte[] PublicKey,
    byte[] EncryptedPrivateKey,
    byte[] Settings);

/// <summary>A new passphrase: the same master key, wrapped again.</summary>
public record UpdateVaultKeyDto(byte[] Salt, int Iterations, byte[] WrappedKey);

/// <param name="ExpectedVersion">The version the change is based on; the server answers 409 if it changed.</param>
public record UpdateVaultSettingsDto(byte[] Settings, int ExpectedVersion);

public record VaultSettingsVersionDto(int SettingsVersion);

/// <summary>Usage of one app on one device within one hour, encrypted.</summary>
/// <param name="Tag">Keyed hash of the app identifier: the same app always gets the same tag for one user, but
/// the server can't tell which app it is.</param>
/// <param name="Hour">Start of the hourly UTC bucket.</param>
public record EncryptedUsageEntryDto(string Tag, DateTime Hour, byte[] Data);

/// <summary>An upload replaces what is stored for each (tag, hour) it contains.</summary>
public record EncryptedUsageUploadDto(List<EncryptedUsageEntryDto> Entries);

/// <param name="TagsMissingInfo">Uploaded tags the server has no app information for yet; the client sends it
/// with <c>PUT /api/e2e/apps</c>.</param>
public record EncryptedUsageUploadResultDto(int Stored, List<string> TagsMissingInfo);

public record EncryptedUsageDto(int DeviceId, string Tag, DateTime Hour, byte[] Data);

/// <summary>Name, category and icon of an app, encrypted.</summary>
public record EncryptedAppDto(string Tag, byte[] Data);

/// <summary>Plaintext data from before end-to-end encryption that the client still has to move.</summary>
public record LegacyStatusDto(int UsageRows, DateTime? First, DateTime? Last, int Apps, int Exclusions);

/// <summary>Removes rows of hours that are now stored encrypted.</summary>
public record DeleteEncryptedUsageDto(List<EncryptedUsageKeyDto> Rows);

public record EncryptedUsageKeyDto(int DeviceId, string Tag, DateTime Hour);

/// <summary>The encrypted profile (real name, phone number, profile picture); <c>Data</c> is null when none is
/// saved.</summary>
public record EncryptedProfileDto(byte[]? Data);
