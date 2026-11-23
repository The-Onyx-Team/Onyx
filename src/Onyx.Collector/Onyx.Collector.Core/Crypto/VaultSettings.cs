using System.Text.Json.Serialization;

namespace Onyx.Collector.Core.Crypto;

/// <summary>
/// The user's settings, stored as one encrypted document (<c>VaultDto.Settings</c>). Shared by all clients:
/// the dashboards read and edit everything, the desktop collectors read <see cref="ExcludedDomains"/>.
/// </summary>
public sealed class VaultSettings
{
    /// <summary>The user's own names, categories and hidden flags, by app identifier.</summary>
    [JsonPropertyName("apps")]
    public Dictionary<string, AppSettings> Apps { get; set; } = new();

    /// <summary>The user's categories in the order shown. New vaults start with <see cref="DefaultCategories"/>.</summary>
    [JsonPropertyName("categories")]
    public List<CategorySettings> Categories { get; set; } = [];

    /// <summary>Websites never recorded (normalized domains; subdomains included).</summary>
    [JsonPropertyName("excludedDomains")]
    public List<string> ExcludedDomains { get; set; } = [];

    [JsonPropertyName("goals")]
    public List<GoalSettings> Goals { get; set; } = [];

    [JsonPropertyName("sharing")]
    public SharingSettings Sharing { get; set; } = new();

    public static VaultSettings CreateDefault() => new()
    {
        Categories = DefaultCategories.Select(c => new CategorySettings { Name = c.Name, Color = c.Color }).ToList()
    };

    /// <summary>Categories a new vault starts with; the user can rename, recolour and delete them.</summary>
    public static readonly IReadOnlyList<(string Name, string Color)> DefaultCategories =
    [
        ("Social", "#B5653A"),
        ("Communication", "#C58848"),
        ("Video", "#9D4D27"),
        ("Music & Audio", "#7A8B6F"),
        ("Games", "#5E3A22"),
        ("Productivity", "#3A5A6B"),
        ("Development", "#494C3B"),
        ("Browsing", "#8A8F98"),
        ("News & Reading", "#D9B38C"),
        ("Other", "#9F9B8A"),
    ];
}

public sealed class AppSettings
{
    /// <summary>Shown instead of the reported name. Apps with the same name are shown as one app.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Not shown on the dashboard and in reports (the data is kept).</summary>
    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Category) && !Hidden;
}

public sealed class CategorySettings
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>CSS colour, <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#9F9B8A";
}

[JsonConverter(typeof(JsonStringEnumConverter<GoalScope>))]
public enum GoalScope
{
    /// <summary>All screen time.</summary>
    Total,
    Device,
    Category,
    App
}

[JsonConverter(typeof(JsonStringEnumConverter<GoalPeriod>))]
public enum GoalPeriod
{
    Day,
    Week
}

[JsonConverter(typeof(JsonStringEnumConverter<GoalDirection>))]
public enum GoalDirection
{
    /// <summary>A limit: stay below <see cref="GoalSettings.Minutes"/>.</summary>
    AtMost,

    /// <summary>A target: reach at least <see cref="GoalSettings.Minutes"/> (e.g. reading or learning apps).</summary>
    AtLeast
}

public sealed class GoalSettings
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("scope")]
    public GoalScope Scope { get; set; }

    /// <summary>Device id, category name or app name (the shown name), depending on <see cref="Scope"/>.</summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("period")]
    public GoalPeriod Period { get; set; }

    [JsonPropertyName("direction")]
    public GoalDirection Direction { get; set; }

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; }

    /// <summary>Day the goal starts counting (local date); streaks never reach back before it.</summary>
    [JsonPropertyName("since")]
    public DateOnly Since { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ShareLevel>))]
public enum ShareLevel
{
    /// <summary>Friends see that the user is there, but no numbers.</summary>
    Nothing,

    /// <summary>Screen time per day and week.</summary>
    Total,

    /// <summary>Also the time per category.</summary>
    Categories,

    /// <summary>Also goals met and streaks.</summary>
    CategoriesAndGoals
}

public sealed class SharingSettings
{
    [JsonPropertyName("level")]
    public ShareLevel Level { get; set; } = ShareLevel.Total;
}
