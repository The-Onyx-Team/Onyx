using System.Security.Cryptography;
using Microsoft.JSInterop;
using Onyx.Collector.Core.Crypto;

namespace Onyx.App.Shared.Services.Client;

/// <summary>
/// <see cref="IOnyxCrypto"/> with the browser's WebCrypto (.NET in WebAssembly has no AES-GCM, PBKDF2 or ECDH).
/// The byte layout is the same as <see cref="DotNetCrypto"/>'s.
/// </summary>
public sealed class BrowserCrypto(OnyxJs js) : IOnyxCrypto
{
    public async Task<byte[]> DeriveKeyAsync(string passphrase, byte[] salt, int iterations) =>
        await js.InvokeAsync<byte[]>("deriveKey", passphrase, salt, iterations);

    public async Task<byte[]> HmacSha256Async(byte[] key, byte[] data) =>
        await js.InvokeAsync<byte[]>("hmac", key, data);

    public async Task<byte[]> EncryptAsync(byte[] key, byte[] plaintext, string context) =>
        await js.InvokeAsync<byte[]>("encrypt", key, plaintext, context);

    public async Task<byte[]> DecryptAsync(byte[] key, byte[] blob, string context)
    {
        try
        {
            return await js.InvokeAsync<byte[]>("decrypt", key, blob, context);
        }
        catch (JSException e)
        {
            throw new CryptographicException("Decryption failed", e);
        }
    }

    public async Task<byte[][]> EncryptManyAsync(byte[] key, IReadOnlyList<(byte[] Plaintext, string Context)> items)
    {
        var result = new List<byte[]>(items.Count);
        foreach (var chunk in items.Chunk(2000))
        {
            var encrypted = await js.InvokeAsync<string[]>("encryptMany", key,
                chunk.Select(i => new Item(Convert.ToBase64String(i.Plaintext), i.Context)).ToArray());
            result.AddRange(encrypted.Select(Convert.FromBase64String));
        }

        return result.ToArray();
    }

    public async Task<byte[]?[]> DecryptManyAsync(byte[] key, IReadOnlyList<(byte[] Blob, string Context)> items)
    {
        var result = new List<byte[]?>(items.Count);
        foreach (var chunk in items.Chunk(2000))
        {
            var decrypted = await js.InvokeAsync<string?[]>("decryptMany", key,
                chunk.Select(i => new Item(Convert.ToBase64String(i.Blob), i.Context)).ToArray());
            result.AddRange(decrypted.Select(d => d is null ? null : Convert.FromBase64String(d)));
        }

        return result.ToArray();
    }

    public async Task<(byte[] PrivateKey, byte[] PublicKey)> CreateKeyPairAsync()
    {
        var pair = await js.InvokeAsync<KeyPair>("createKeyPair");
        return (Convert.FromBase64String(pair.PrivateKey), Convert.FromBase64String(pair.PublicKey));
    }

    public async Task<byte[]> SharedKeyAsync(byte[] privateKey, byte[] publicKey, byte[] otherPublicKey)
    {
        try
        {
            return await js.InvokeAsync<byte[]>("sharedKey", privateKey, publicKey, otherPublicKey);
        }
        catch (JSException e)
        {
            throw new CryptographicException("Invalid key", e);
        }
    }

    private sealed record Item(string Data, string Context);

    private sealed record KeyPair(string PrivateKey, string PublicKey);
}
