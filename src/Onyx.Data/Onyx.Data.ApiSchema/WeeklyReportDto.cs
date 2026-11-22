namespace Onyx.Data.ApiSchema;

/// <summary>
/// Usage of one week (Monday to Sunday in the user's time zone) with comparisons to the week before and a trend
/// over the last <see cref="TrendWeeks"/> weeks. Charts are SVG images rendered by the server.
/// </summary>
public record WeeklyReportDto
{
    public const int TrendWeeks = 8;

    public DateOnly WeekStart { get; init; }
    public TimeSpan Total { get; init; }
    public TimeSpan PreviousTotal { get; init; }

    /// <summary>Average per day over the days of the week that have passed.</summary>
    public TimeSpan DailyAverage { get; init; }

    public string? BusiestDay { get; init; }
    public TimeSpan BusiestDayTime { get; init; }

    public List<ReportItemDto> Apps { get; init; } = [];
    public List<ReportItemDto> Categories { get; init; } = [];
    public List<ReportDeviceDto> Devices { get; init; } = [];

    /// <summary>Weekly totals, oldest first, ending with this week.</summary>
    public List<ReportItemDto> Trend { get; init; } = [];

    /// <summary>SVG charts by key: <c>daily</c>, <c>apps</c>, <c>categories</c>, <c>trend</c>, <c>heatmap</c>,
    /// and <c>device-{id}</c> per device.</summary>
    public Dictionary<string, string> Charts { get; init; } = [];

    /// <summary>Why charts are missing, e.g. Python or matplotlib not installed on the server.</summary>
    public string? ChartError { get; init; }
}

/// <param name="Name">App, category or week label.</param>
/// <param name="Time">Time in this week.</param>
/// <param name="PreviousTime">Time in the week before.</param>
public record ReportItemDto(string Name, TimeSpan Time, TimeSpan PreviousTime);

public record ReportDeviceDto(int Id, string Name, TimeSpan Time, TimeSpan PreviousTime, List<ReportItemDto> Apps);
