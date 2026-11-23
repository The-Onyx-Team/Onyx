using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core.Crypto;

/// <summary>
/// The keys of one user. Everything is derived from a random 32-byte master key, which the server only stores
/// encrypted with the user's passphrase (<see cref="VaultDto.WrappedKey"/>). The recovery key is the master key
/// itself, written out (<see cref="RecoveryKeys"/>).
/// </summary>
public sealed class VaultKeys
{
    private VaultKeys(byte[] masterKey, byte[] encryptionKey, byte[] tagKey)
    {
        MasterKey = masterKey;
        EncryptionKey = encryptionKey;
        TagKey = tagKey;
    }

    public byte[] MasterKey { get; }

    /// <summary>AES-256-GCM key for usage, apps, settings and the private key.</summary>
    public byte[] EncryptionKey { get; }

    /// <summary>HMAC key that turns app identifiers into tags.</summary>
    public byte[] TagKey { get; }

    public static async Task<VaultKeys> FromMasterKeyAsync(IOnyxCrypto crypto, byte[] masterKey)
    {
        if (masterKey.Length != CryptoFormat.KeySize)
            throw new CryptographicException("A master key has 32 bytes");
        return new VaultKeys(masterKey,
            await crypto.HmacSha256Async(masterKey, "onyx/encryption/v1"u8.ToArray()),
            await crypto.HmacSha256Async(masterKey, "onyx/tag/v1"u8.ToArray()));
    }
}

/// <summary>The associated data that binds each encrypted blob to its place, so the server can't swap blobs
/// between rows, users or purposes without decryption failing.</summary>
public static class E2EContext
{
    public const string MasterKey = "onyx/masterkey";
    public const string PrivateKey = "onyx/privatekey";
    public const string Settings = "onyx/settings";

    public static string Usage(int deviceId, string tag, DateTime hour) =>
        string.Create(CultureInfo.InvariantCulture,
            $"onyx/usage|{deviceId}|{tag}|{new DateTimeOffset(DateTime.SpecifyKind(hour, DateTimeKind.Utc)).ToUnixTimeSeconds()}");

    public static string App(string tag) => $"onyx/app|{tag}";

    public static string Share(string ownerId, string recipientId, DateOnly periodStart) =>
        $"onyx/share|{ownerId}|{recipientId}|{periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The master key written out for the user to keep: 52 characters of Crockford base32 in groups of four. Losing
/// both the passphrase and the recovery key means the data can't be decrypted any more, by anyone.
/// </summary>
public static class RecoveryKeys
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(byte[] key)
    {
        var text = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in key)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            text.Append(Alphabet[(buffer << (5 - bits)) & 31]);

        return string.Join('-', text.ToString().Chunk(4).Select(c => new string(c)));
    }

    /// <returns>Null if the text is not a recovery key.</returns>
    public static byte[]? Decode(string text)
    {
        var clean = text.ToUpperInvariant()
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })
            .ToArray();
        if (clean.Length != 52)
            return null;

        var key = new List<byte>(CryptoFormat.KeySize);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                key.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return key.Count == CryptoFormat.KeySize ? key.ToArray() : null;
    }
}
