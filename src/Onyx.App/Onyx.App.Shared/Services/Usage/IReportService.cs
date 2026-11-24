using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>Weekly usage reports with charts, built on this device from the decrypted data.</summary>
public interface IReportService
{
    /// <param name="weekStart">Monday of the week, in <paramref name="zone"/>.</param>
    Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, TimeZoneInfo zone);

    /// <summary>The same report as an A4 PDF.</summary>
    Task<byte[]> GetWeeklyReportPdfAsync(DateOnly weekStart, TimeZoneInfo zone);
}
