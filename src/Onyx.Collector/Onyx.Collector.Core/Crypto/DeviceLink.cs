using System.Security.Cryptography;

namespace Onyx.Collector.Core.Crypto;

/// <summary>
/// Linking a device without a keyboard (a watch) to an account: the new device creates an ECDH key pair and shows a
/// code; the user enters it on a device that is signed in and unlocked, which sends the new device its tokens and
/// the master key, encrypted for its public key.
/// </summary>
/// <remarks>
/// The code is a fingerprint of the new device's public key (50 bits of its SHA-256). The signed-in device checks
/// the public key the server hands it against the code, so a server can't put in a key of its own: it would have
/// to find a key with the same fingerprint within the few minutes a code is valid.
/// </remarks>
public static class DeviceLink
{
    /// <summary>How long a code can be entered.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public const int CodeLength = 10;

    /// <summary>Crockford's base32: no I, L, O or U, so codes are easy to read off a small screen.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The code for a public key, e.g. <c>7K2QM-X9D4R</c> (shown with the dash, compared without).</summary>
    public static string CodeOf(byte[] publicKey)
    {
        var hash = SHA256.HashData(publicKey);
        var bits = 0UL;
        for (var i = 0; i < 8; i++)
            bits = (bits << 8) | hash[i];
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Alphabet[(int)((bits >> (64 - 5 * (i + 1))) & 31)];
        return new string(chars);
    }

    public static string Display(string code) => code.Length == CodeLength ? $"{code[..5]}-{code[5..]}" : code;

    /// <summary>A typed code in the form <see cref="CodeOf"/> returns: upper case, without dashes and spaces, with
    /// the letters people confuse with digits mapped like Crockford's base32 does.</summary>
    public static string Normalize(string? typed) => new((typed ?? "").ToUpperInvariant()
        .Where(c => !char.IsWhiteSpace(c) && c != '-')
        .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })
        .ToArray());

    /// <summary>Associated data of the encrypted master key.</summary>
    public static string Context(string code) => $"link|{code}";
}
