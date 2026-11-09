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
