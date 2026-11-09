namespace Onyx.Data.ApiSchema;

public class AppDto
{
    public int Id { get; set; }
    public string? Identifier { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }

    /// <summary>PNG icon, empty when none has been uploaded.</summary>
    public required byte[] IconBitmap { get; set; }
}

public record AppIconDto(string AppIdentifier, byte[] IconPng);
