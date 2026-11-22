using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Api;

public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/reports")
            .WithTags("Reports API")
            .RequireAuthorization(Program.ApiPolicy);

        group.MapGet("/weekly", GetWeeklyReportAsync).Produces<WeeklyReportDto>();

        return group;
    }

    /// <param name="weekStart">Any day of the week (yyyy-MM-dd); the report starts on its Monday.</param>
    /// <param name="timeZone">IANA time zone of the viewer, e.g. <c>Europe/Vienna</c>.</param>
    public static async Task<IResult> GetWeeklyReportAsync(
        [FromQuery] DateOnly weekStart,
        [FromQuery] string timeZone,
        [FromServices] IReportService reports)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
            return Results.BadRequest($"Unknown time zone '{timeZone}'");

        return Results.Ok(await reports.GetWeeklyReportAsync(weekStart, zone));
    }
}
