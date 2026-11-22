using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>Weekly usage reports with charts (rendered on the server with Python and matplotlib).</summary>
public interface IReportService
{
    /// <param name="weekStart">Monday of the week, in <paramref name="zone"/>.</param>
    Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, TimeZoneInfo zone);
}
