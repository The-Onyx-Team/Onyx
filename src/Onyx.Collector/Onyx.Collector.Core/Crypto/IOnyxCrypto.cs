namespace Onyx.Collector.Core.Crypto;

/// <summary>
/// The cryptographic primitives of Onyx's end-to-end encryption. <see cref="DotNetCrypto"/> implements them with
/// .NET (apps, Linux agent, server tests); the browser uses WebCrypto through JS interop, because .NET in
/// WebAssembly has no AES-GCM, PBKDF2 or ECDH. Both produce the same bytes, see <see cref="CryptoFormat"/>.
/// </summary>
/// <remarks>Asynchronous because WebCrypto is.</remarks>
public interface IOnyxCrypto
{
    /// <summary>PBKDF2-HMAC-SHA256, 32 bytes.</summary>
    Task<byte[]> DeriveKeyAsync(string passphrase, byte[] salt, int iterations);

    Task<byte[]> HmacSha256Async(byte[] key, byte[] data);

    /// <summary>AES-256-GCM with a random nonce; <paramref name="context"/> is authenticated, not stored.</summary>
    /// <returns>A blob in the format of <see cref="CryptoFormat"/>.</returns>
    Task<byte[]> EncryptAsync(byte[] key, byte[] plaintext, string context);

    /// <exception cref="System.Security.Cryptography.CryptographicException">Wrong key or context, or the blob
    /// was changed.</exception>
    Task<byte[]> DecryptAsync(byte[] key, byte[] blob, string context);

    /// <summary>Encrypts many items with one key (one interop call in the browser).</summary>
    Task<byte[][]> EncryptManyAsync(byte[] key, IReadOnlyList<(byte[] Plaintext, string Context)> items);

    /// <summary>Decrypts many items with one key; an item that fails to decrypt comes back as null.</summary>
    Task<byte[]?[]> DecryptManyAsync(byte[] key, IReadOnlyList<(byte[] Blob, string Context)> items);

    /// <summary>A new ECDH P-256 key pair: the private scalar (32 bytes) and the uncompressed public point
    /// (65 bytes, <c>0x04 || X || Y</c>).</summary>
    Task<(byte[] PrivateKey, byte[] PublicKey)> CreateKeyPairAsync();

    /// <summary>The AES key two users share: SHA-256 of the ECDH secret of one's private and the other's public
    /// key. Both sides get the same key.</summary>
    Task<byte[]> SharedKeyAsync(byte[] privateKey, byte[] publicKey, byte[] otherPublicKey);
}

/// <summary>Byte layout shared by every implementation.</summary>
public static class CryptoFormat
{
    /// <summary>First byte of every blob.</summary>
    public const byte Version = 1;

    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;
    public const int Overhead = 1 + NonceSize + TagSize;

    /// <summary>PBKDF2 iterations for new passphrases (OWASP 2023 recommendation for PBKDF2-HMAC-SHA256).</summary>
    public const int Iterations = 600_000;

    public const int SaltSize = 16;
}
