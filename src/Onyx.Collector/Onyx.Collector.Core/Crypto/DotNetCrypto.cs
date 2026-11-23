using System.Security.Cryptography;
using System.Text;

namespace Onyx.Collector.Core.Crypto;

/// <summary><see cref="IOnyxCrypto"/> with .NET's cryptography (Android, Windows, Linux).</summary>
public sealed class DotNetCrypto : IOnyxCrypto
{
    public static readonly DotNetCrypto Instance = new();

    public Task<byte[]> DeriveKeyAsync(string passphrase, byte[] salt, int iterations) =>
        Task.FromResult(DeriveKey(passphrase, salt, iterations));

    public static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256,
            CryptoFormat.KeySize);

    public Task<byte[]> HmacSha256Async(byte[] key, byte[] data) => Task.FromResult(HMACSHA256.HashData(key, data));

    public Task<byte[]> EncryptAsync(byte[] key, byte[] plaintext, string context) =>
        Task.FromResult(Encrypt(key, plaintext, context));

    public Task<byte[]> DecryptAsync(byte[] key, byte[] blob, string context) =>
        Task.FromResult(Decrypt(key, blob, context));

    public Task<byte[][]> EncryptManyAsync(byte[] key, IReadOnlyList<(byte[] Plaintext, string Context)> items)
    {
        using var aes = new AesGcm(key, CryptoFormat.TagSize);
        return Task.FromResult(items.Select(i => Encrypt(aes, i.Plaintext, i.Context)).ToArray());
    }

    public Task<byte[]?[]> DecryptManyAsync(byte[] key, IReadOnlyList<(byte[] Blob, string Context)> items)
    {
        using var aes = new AesGcm(key, CryptoFormat.TagSize);
        return Task.FromResult(items.Select(i =>
        {
            try
            {
                return Decrypt(aes, i.Blob, i.Context);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }).ToArray());
    }

    public Task<(byte[] PrivateKey, byte[] PublicKey)> CreateKeyPairAsync()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdh.ExportParameters(includePrivateParameters: true);
        return Task.FromResult((Pad(parameters.D!), PublicKeyBytes(parameters.Q)));
    }

    public Task<byte[]> SharedKeyAsync(byte[] privateKey, byte[] publicKey, byte[] otherPublicKey)
    {
        using var mine = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey,
            Q = Point(publicKey)
        });
        using var other = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = Point(otherPublicKey)
        });
        // SHA-256 of the shared secret's X coordinate, what WebCrypto's deriveBits followed by a digest gives.
        return Task.FromResult(mine.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256));
    }

    public static byte[] Encrypt(byte[] key, byte[] plaintext, string context)
    {
        using var aes = new AesGcm(key, CryptoFormat.TagSize);
        return Encrypt(aes, plaintext, context);
    }

    public static byte[] Decrypt(byte[] key, byte[] blob, string context)
    {
        using var aes = new AesGcm(key, CryptoFormat.TagSize);
        return Decrypt(aes, blob, context);
    }

    private static byte[] Encrypt(AesGcm aes, byte[] plaintext, string context)
    {
        var blob = new byte[CryptoFormat.Overhead + plaintext.Length];
        blob[0] = CryptoFormat.Version;
        var nonce = blob.AsSpan(1, CryptoFormat.NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = blob.AsSpan(1 + CryptoFormat.NonceSize, plaintext.Length);
        var tag = blob.AsSpan(1 + CryptoFormat.NonceSize + plaintext.Length, CryptoFormat.TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(context));
        return blob;
    }

    private static byte[] Decrypt(AesGcm aes, byte[] blob, string context)
    {
        if (blob.Length < CryptoFormat.Overhead || blob[0] != CryptoFormat.Version)
            throw new CryptographicException("Not an Onyx encrypted blob");

        var length = blob.Length - CryptoFormat.Overhead;
        var plaintext = new byte[length];
        aes.Decrypt(blob.AsSpan(1, CryptoFormat.NonceSize),
            blob.AsSpan(1 + CryptoFormat.NonceSize, length),
            blob.AsSpan(1 + CryptoFormat.NonceSize + length, CryptoFormat.TagSize),
            plaintext,
            Encoding.UTF8.GetBytes(context));
        return plaintext;
    }

    private static byte[] PublicKeyBytes(ECPoint q) => [0x04, .. Pad(q.X!), .. Pad(q.Y!)];

    private static ECPoint Point(byte[] publicKey)
    {
        if (publicKey.Length != 65 || publicKey[0] != 0x04)
            throw new CryptographicException("Not an uncompressed P-256 public key");
        return new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] };
    }

    /// <summary>Coordinates and scalars are 32 bytes; some platforms drop leading zeros.</summary>
    private static byte[] Pad(byte[] value) =>
        value.Length >= 32 ? value : [.. new byte[32 - value.Length], .. value];
}
