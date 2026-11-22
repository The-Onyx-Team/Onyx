namespace Onyx.Data.ApiSchema;

public class AppDto
{
    public int Id { get; set; }
    public string? Identifier { get; set; }
    /// <summary>Name shown to the user: their own name for the app if they set one, otherwise the reported one.</summary>
    public required string Name { get; set; }

    /// <summary>Category shown to the user: their own if set, otherwise the reported one.</summary>
    public string? Category { get; set; }

    /// <summary>Name and category as reported by the devices, before the user's own (<see cref="AppOverrideDto"/>).</summary>
    public string? ReportedName { get; set; }
    public string? ReportedCategory { get; set; }

    /// <summary>Whether the user set their own name or category.</summary>
    public bool IsCustomized { get; set; }

    /// <summary>PNG icon, empty when none has been uploaded.</summary>
    public required byte[] IconBitmap { get; set; }
}

public record AppIconDto(string AppIdentifier, byte[] IconPng);

/// <summary>The user's own name and category for an app. Null or empty values keep the reported ones; both empty
/// removes the override.</summary>
public record AppOverrideDto(string? DisplayName, string? Category);
