using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Api;

public static class DataEndpoints
{
    public static IEndpointRouteBuilder MapDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dataApi = endpoints.MapGroup("/api/data")
            .WithTags("Data API");
        
        dataApi.MapGet("/usage", GetUsageDataAsync)
            .WithName("GetUsageData")
            .Produces<List<UsageDto>>();

        dataApi.MapGet("/usage/device/{deviceId:int}", GetUsageDataForDeviceAsync)
            .WithName("GetUsageDataForDevice")
            .Produces<List<UsageDto>>();

        dataApi.MapPost("/usage/upload", UploadUsageData)
            .WithName("UploadUsageData")
            .Produces<bool>();

        return endpoints;
    }

    private static async Task<IResult> GetUsageDataAsync(
        [FromServices] IUsageDataService usageDataService,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var data = await usageDataService.GetUsageDataAsync(startTime, endTime);
        return Results.Ok(data);
    }

    private static async Task<IResult> GetUsageDataForDeviceAsync(
        [FromServices] IUsageDataService usageDataService,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        [FromRoute] int deviceId)
    {
        var data = await usageDataService.GetUsageDataForDeviceAsync(startTime, endTime, deviceId);
        return Results.Ok(data);
    }

    [Authorize(AuthenticationSchemes = "JwtSchema")]
    private static async Task<IResult> UploadUsageData(
        [FromServices] IUsageDataService usageDataService,
        [FromBody] List<UsageDto> usageData,
        [FromQuery] int deviceId)
    {
        var result = await usageDataService.UploadUsageData(usageData, deviceId);
        return Results.Ok(new {result});
    }
}