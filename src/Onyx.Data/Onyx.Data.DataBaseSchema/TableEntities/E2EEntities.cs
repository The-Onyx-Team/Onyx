using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

// End-to-end encrypted data. The server stores these blobs but has no key to read them; see the client's
// UserVault and docs/Writerside/topics/Encryption.md.

/// <summary>A user's key material and encrypted settings. One row per user, created when they set up
/// encryption.</summary>
[Table("UserVaults")]
public class UserVaultEntity
{
    [Key, Column("UserId"), StringLength(450)]
    public string UserId { get; set; } = "";

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(64)] public byte[] Salt { get; set; } = [];

    public int Iterations { get; set; }

    /// <summary>The master key, encrypted with the key derived from the passphrase.</summary>
    [Required, MaxLength(256)] public byte[] WrappedKey { get; set; } = [];

    /// <summary>ECDH P-256 public key for sharing with friends (not secret).</summary>
    [Required, MaxLength(65)] public byte[] PublicKey { get; set; } = [];

    [Required, MaxLength(256)] public byte[] EncryptedPrivateKey { get; set; } = [];

    /// <summary>Encrypted settings document (app names, categories, goals, excluded websites, sharing).</summary>
    [Required] public byte[] Settings { get; set; } = [];

    /// <summary>Increases with every settings change (optimistic concurrency between devices).</summary>
    public int SettingsVersion { get; set; }

    /// <summary>Encrypted profile: real name, phone number and profile picture. Null until the user saves one.
    /// (User name and email stay readable: they are how people sign in and find each other.)</summary>
    public byte[]? Profile { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>One app's usage on one device in one hour, encrypted. Replaces <see cref="Usage"/>.</summary>
[Table("EncryptedUsage")]
public class EncryptedUsage
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>Keyed hash of the app identifier (base64url, 22 characters).</summary>
    [Required, StringLength(32)] public string Tag { get; set; } = "";

    /// <summary>Start of the hourly UTC bucket. Not encrypted, so uploads can replace hours and the dashboard
    /// can ask for a time range.</summary>
    public DateTime Hour { get; set; }

    [Required] public byte[] Data { get; set; } = [];
}

/// <summary>Name, category and icon of one of a user's apps, encrypted.</summary>
[Table("EncryptedApps")]
public class EncryptedApp
{
    [StringLength(450)] public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    [StringLength(32)] public string Tag { get; set; } = "";

    [Required] public byte[] Data { get; set; } = [];

    public DateTime UpdatedAt { get; set; }
}

/// <summary>A friend request, or a friendship once accepted. Always stored once, from the requester's side.</summary>
[Table("Friendships")]
public class Friendship
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, StringLength(450)] public string RequesterId { get; set; } = "";
    public ApplicationUser? Requester { get; set; }

    [Required, StringLength(450)] public string AddresseeId { get; set; } = "";
    public ApplicationUser? Addressee { get; set; }

    public bool Accepted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}

/// <summary>Numbers one user shares with another for one week, encrypted for the recipient.</summary>
[Table("SharedStats")]
public class SharedStat
{
    [StringLength(450)] public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    [StringLength(450)] public string RecipientId { get; set; } = "";
    public ApplicationUser? Recipient { get; set; }

    /// <summary>Monday of the week.</summary>
    public DateOnly PeriodStart { get; set; }

    [Required] public byte[] Data { get; set; } = [];

    public DateTime UpdatedAt { get; set; }
}
