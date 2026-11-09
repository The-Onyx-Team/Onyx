using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Api;

public static class DataEndpoints
{
    public static IEndpointRouteBuilder MapDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dataApi = endpoints.MapGroup("/api/data")
            .WithTags("Data API")
            .RequireAuthorization(Program.ApiPolicy);

        dataApi.MapGet("/usage", GetUsageDataAsync)
            .WithName("GetUsageData")
            .Produces<List<UsageDto>>();

        return endpoints;
    }

    public static async Task<IResult> GetUsageDataAsync(
        [FromServices] IUsageDataService usageDataService,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        [FromQuery] int? deviceId)
    {
        if (endTime <= startTime)
            return Results.BadRequest("endTime must be after startTime");

        return Results.Ok(await usageDataService.GetUsageDataAsync(startTime, endTime, deviceId));
    }
}
