using System.Globalization;

namespace Onyx.App.Shared.Services.Dashboard;

/// <summary>Weeks as Onyx shows them: Monday to Sunday, numbered by ISO 8601 (week 1 holds the first Thursday).</summary>
public static class Weeks
{
    public static DateOnly StartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static int Number(DateOnly day) => ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>The year the week belongs to (the first days of January can belong to the last week of the
    /// year before).</summary>
    public static int Year(DateOnly day) => ISOWeek.GetYear(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>"Week 47, 2026"</summary>
    public static string Label(DateOnly day) => $"Week {Number(day)}, {Year(day)}";

    /// <summary>"Week 47 · 16 – 22 Nov 2026"</summary>
    public static string LongLabel(DateOnly weekStart)
    {
        var end = weekStart.AddDays(6);
        var culture = CultureInfo.InvariantCulture;
        var range = weekStart.Month == end.Month
            ? $"{weekStart.Day} – {end.ToString("d MMM yyyy", culture)}"
            : weekStart.Year == end.Year
                ? $"{weekStart.ToString("d MMM", culture)} – {end.ToString("d MMM yyyy", culture)}"
                : $"{weekStart.ToString("d MMM yyyy", culture)} – {end.ToString("d MMM yyyy", culture)}";
        return $"Week {Number(weekStart)} · {range}";
    }
}
