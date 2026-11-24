using System.Text.Json.Serialization;

namespace Onyx.Collector.Core.Crypto;

/// <summary>
/// The user's settings, stored as one encrypted document (<c>VaultDto.Settings</c>). Shared by all clients:
/// the dashboards read and edit everything; the collectors read <see cref="ExcludedDomains"/>,
/// <see cref="Reminders"/>, <see cref="Focus"/> and the categories in <see cref="Apps"/>.
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

    /// <summary>Notifications on the devices, e.g. after an hour without a break.</summary>
    [JsonPropertyName("reminders")]
    public List<ReminderSettings> Reminders { get; set; } = [];

    [JsonPropertyName("focus")]
    public FocusSettings Focus { get; set; } = new();

    /// <summary>Reports the user put together (period, devices, apps, sections), to run again any time.</summary>
    [JsonPropertyName("customReports")]
    public List<CustomReportSettings> CustomReports { get; set; } = [];

    /// <summary>The category an app is counted in: the user's own, otherwise the one its device reported.</summary>
    public string? CategoryOf(string identifier, string? reportedCategory) =>
        Apps.TryGetValue(identifier, out var app) && !string.IsNullOrWhiteSpace(app.Category)
            ? app.Category.Trim()
            : MapReportedCategory(reportedCategory);

    /// <summary>Platform categories (Android's, the freedesktop ones) mapped to the default categories.</summary>
    public static string? MapReportedCategory(string? reported) => reported switch
    {
        null or "" => null,
        "Game" => "Games",
        "Audio" => "Music & Audio",
        "Video" or "AudioVideo" => "Video",
        "Social" => "Social",
        "News" => "News & Reading",
        "Productivity" or "Office" or "Education" or "Graphics" => "Productivity",
        "Development" => "Development",
        "Network" => "Browsing",
        "Image" or "Maps" or "Accessibility" or "Settings" or "System" or "Utility" or "Science" => "Other",
        _ => reported
    };

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

/// <summary>Which apps a reminder or focus mode is about: apps (identifiers) and categories. Both empty means
/// <paramref name="emptyMatchesAll"/> decides.</summary>
public sealed class AppSelection
{
    /// <summary>App identifiers (all identifiers of an app shown as one, e.g. Firefox on Android and Linux).</summary>
    [JsonPropertyName("apps")]
    public List<string> Apps { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Apps.Count == 0 && Categories.Count == 0;

    public bool Matches(string identifier, string? category, bool emptyMatchesAll) =>
        IsEmpty
            ? emptyMatchesAll
            : Apps.Contains(identifier, StringComparer.Ordinal)
              || (category is not null && Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
}

[JsonConverter(typeof(JsonStringEnumConverter<ReminderKind>))]
public enum ReminderKind
{
    /// <summary>After <see cref="ReminderSettings.Minutes"/> of use without a break, and again every as long.</summary>
    Continuous,

    /// <summary>Once a day, when the time today on the device reaches <see cref="ReminderSettings.Minutes"/>.</summary>
    Daily
}

public sealed class ReminderSettings
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("kind")]
    public ReminderKind Kind { get; set; }

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; } = 60;

    /// <summary>Apps and categories counted; empty: all apps.</summary>
    [JsonPropertyName("apps")]
    public AppSelection Apps { get; set; } = new();

    /// <summary>Devices that remind; empty: all.</summary>
    [JsonPropertyName("devices")]
    public List<int> Devices { get; set; } = [];

    /// <summary>The user's own text for the notification.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Focus mode: while it is on, the devices nudge the user whenever a distracting app comes to the front. It is on
/// until <see cref="Until"/> (started by hand on any device) or during one of the <see cref="Schedules"/>.
/// </summary>
public sealed class FocusSettings
{
    /// <summary>Started by hand: on until this time.</summary>
    [JsonPropertyName("until")]
    public DateTimeOffset? Until { get; set; }

    [JsonPropertyName("schedules")]
    public List<FocusSchedule> Schedules { get; set; } = [];

    /// <summary>Apps and categories that interrupt focus; empty: none (focus mode only shows on the dashboard).</summary>
    [JsonPropertyName("distracting")]
    public AppSelection Distracting { get; set; } = new()
    {
        Categories = ["Social", "Video", "Games"]
    };

    /// <summary>Devices that nudge; empty: all.</summary>
    [JsonPropertyName("devices")]
    public List<int> Devices { get; set; } = [];

    /// <summary>Whether focus mode is on at <paramref name="now"/>, with schedules in <paramref name="zone"/>.</summary>
    /// <returns>Until when it stays on, or null if it is off.</returns>
    public DateTimeOffset? ActiveUntil(DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTimeOffset? end = Until > now ? Until : null;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        foreach (var schedule in Schedules)
        {
            if (schedule.EndOf(local) is { } scheduleEnd && (end is null || scheduleEnd > end))
                end = scheduleEnd;
        }

        return end;
    }

    public bool AppliesTo(int? deviceId) => Devices.Count == 0 || (deviceId is { } id && Devices.Contains(id));
}

/// <summary>Focus on certain days between two times (crossing midnight if <see cref="End"/> is earlier).</summary>
public sealed class FocusSchedule
{
    [JsonPropertyName("days")]
    public List<DayOfWeek> Days { get; set; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    [JsonPropertyName("start")]
    public TimeOnly Start { get; set; } = new(9, 0);

    [JsonPropertyName("end")]
    public TimeOnly End { get; set; } = new(12, 0);

    /// <summary>The end of the period <paramref name="local"/> is in, or null if it is in none.</summary>
    public DateTimeOffset? EndOf(DateTimeOffset local)
    {
        var time = TimeOnly.FromTimeSpan(local.TimeOfDay);
        var today = local.Date;
        if (Start <= End)
        {
            return Days.Contains(local.DayOfWeek) && time >= Start && time < End
                ? new DateTimeOffset(today + End.ToTimeSpan(), local.Offset)
                : null;
        }

        // Crosses midnight: from Start on a listed day until End the next morning.
        if (Days.Contains(local.DayOfWeek) && time >= Start)
            return new DateTimeOffset(today.AddDays(1) + End.ToTimeSpan(), local.Offset);
        if (Days.Contains(local.AddDays(-1).DayOfWeek) && time < End)
            return new DateTimeOffset(today + End.ToTimeSpan(), local.Offset);
        return null;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ReportPeriod>))]
public enum ReportPeriod
{
    Last7Days,
    Last30Days,
    ThisMonth,
    LastMonth,
    Last3Months,
    ThisYear,

    /// <summary><see cref="CustomReportSettings.From"/> to <see cref="CustomReportSettings.To"/>.</summary>
    Custom
}

/// <summary>A report the user put together.</summary>
public sealed class CustomReportSettings
{
    /// <summary>What a report can show, in this order.</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> AllSections =
    [
        ("daily", "Every day"),
        ("apps", "Top apps"),
        ("websites", "Top websites"),
        ("categories", "Categories"),
        ("trend", "Weekly trend"),
        ("heatmap", "Time of day"),
        ("devices", "Each device")
    ];

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "My report";

    [JsonPropertyName("period")]
    public ReportPeriod Period { get; set; } = ReportPeriod.Last30Days;

    [JsonPropertyName("from")]
    public DateOnly? From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly? To { get; set; }

    /// <summary>Devices included; empty: all.</summary>
    [JsonPropertyName("devices")]
    public List<int> Devices { get; set; } = [];

    /// <summary>Apps and categories included; empty: all.</summary>
    [JsonPropertyName("apps")]
    public AppSelection Apps { get; set; } = new();

    /// <summary>Keys from <see cref="AllSections"/>.</summary>
    [JsonPropertyName("sections")]
    public List<string> Sections { get; set; } = ["daily", "apps", "categories", "heatmap"];

    /// <summary>The first and last day (both included) for <paramref name="today"/>.</summary>
    public (DateOnly From, DateOnly To) Range(DateOnly today)
    {
        var month = new DateOnly(today.Year, today.Month, 1);
        return Period switch
        {
            ReportPeriod.Last7Days => (today.AddDays(-6), today),
            ReportPeriod.Last30Days => (today.AddDays(-29), today),
            ReportPeriod.ThisMonth => (month, today),
            ReportPeriod.LastMonth => (month.AddMonths(-1), month.AddDays(-1)),
            ReportPeriod.Last3Months => (month.AddMonths(-2), today),
            ReportPeriod.ThisYear => (new DateOnly(today.Year, 1, 1), today),
            _ => From is { } from && To is { } to && to >= from
                ? (from, to > from.AddDays(365) ? from.AddDays(365) : to)
                : (today.AddDays(-6), today)
        };
    }
}
