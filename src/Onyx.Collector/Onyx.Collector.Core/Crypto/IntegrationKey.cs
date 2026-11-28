using System.Security.Cryptography;
using System.Text;

namespace Onyx.Collector.Core.Crypto;

/// <summary>
/// The key of an integration (n8n, a script, an AI assistant, ...): a read-only share of the user's numbers. The
/// user's devices encrypt a snapshot for it; the integration downloads the snapshot from the server and decrypts it
/// itself. The server only ever sees the access token's hash and the ciphertext.
/// </summary>
/// <remarks>
/// The key string is <c>onyx1.&lt;id&gt;.&lt;secret&gt;</c> (base64url). Two values are derived from the secret
/// with HMAC-SHA256, so one string is all an integration needs:
/// <list type="bullet">
/// <item>the access token, <c>HMAC(secret, "onyx-integration|auth")</c>, sent as
/// <c>Authorization: Bearer &lt;id&gt;.&lt;token&gt;</c> (base64url);</item>
/// <item>the encryption key, <c>HMAC(secret, "onyx-integration|key")</c>, for AES-256-GCM in the
/// <see cref="CryptoFormat"/> layout with <see cref="Context"/> as associated data.</item>
/// </list>
/// </remarks>
public sealed record IntegrationKey(string Id, byte[] Secret)
{
    public const string Prefix = "onyx1";
    public const int IdBytes = 16;
    public const int SecretBytes = 32;

    /// <summary>Length of a base64url id of <see cref="IdBytes"/> bytes.</summary>
    public const int IdLength = 22;

    public static IntegrationKey Create() =>
        new(Base64Url(RandomNumberGenerator.GetBytes(IdBytes)), RandomNumberGenerator.GetBytes(SecretBytes));

    public override string ToString() => $"{Prefix}.{Id}.{Base64Url(Secret)}";

    public static bool TryParse(string? text, out IntegrationKey key)
    {
        key = null!;
        var parts = text?.Trim().Split('.');
        if (parts is not [Prefix, { Length: IdLength } id, var secret] || !IsBase64Url(id))
            return false;
        try
        {
            var bytes = FromBase64Url(secret);
            if (bytes.Length != SecretBytes)
                return false;
            key = new IntegrationKey(id, bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Associated data of the snapshot blob.</summary>
    public string Context => $"integration|{Id}";

    public async Task<string> AccessTokenAsync(IOnyxCrypto crypto) =>
        Base64Url(await crypto.HmacSha256Async(Secret, "onyx-integration|auth"u8.ToArray()));

    public Task<byte[]> EncryptionKeyAsync(IOnyxCrypto crypto) =>
        crypto.HmacSha256Async(Secret, "onyx-integration|key"u8.ToArray());

    /// <summary>What the server stores to check an access token.</summary>
    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool IsBase64Url(string text) =>
        text.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
