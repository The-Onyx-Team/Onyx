namespace Onyx.Data.ApiSchema;

public class DeviceDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Platform { get; set; }

    /// <summary>Last successful upload (UTC), null if the device never uploaded.</summary>
    public DateTime? LastSeen { get; set; }
}

public record CreateDeviceDto(string Name, string? Platform);

/// <summary>A device without a keyboard asks to be linked; <paramref name="PublicKey"/> is its ECDH P-256 key
/// (65 bytes, uncompressed).</summary>
public record StartLinkDto(byte[] PublicKey);

/// <param name="Code">To show to the user (without the dash).</param>
/// <param name="PollToken">Proves to the server that a poll comes from the device that started the link.</param>
public record LinkStartedDto(string Code, string PollToken, DateTime ExpiresAt);

/// <summary>What the signed-in device sees for a code: the new device's public key, to check against the code.</summary>
public record LinkInfoDto(byte[] PublicKey);

/// <summary>The signed-in device's answer: its own one-time public key and the master key encrypted with the
/// ECDH key of both.</summary>
public record CompleteLinkDto(byte[] PublicKey, byte[] EncryptedKey);

public record PollLinkDto(string PollToken);

/// <summary>What the new device receives once the user entered the code: tokens of its own and the encrypted key.</summary>
public record LinkResultDto(string Token, string RefreshToken, byte[] PublicKey, byte[] EncryptedKey);
