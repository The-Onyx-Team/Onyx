using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.Web.Api;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dataApi = endpoints.MapGroup("/api/devices")
            .WithTags("Devices API")
            .RequireAuthorization("api");

        dataApi.MapGet("/", GetDevicesAsync);
        dataApi.MapPost("/", RegisterDeviceAsync);
        dataApi.MapDelete("/", UnRegisterDeviceAsync);

        return endpoints;
    }

    private static async Task<IResult> UnRegisterDeviceAsync(
        [FromQuery] int id,
        [FromServices] IDeviceManager deviceManager)
    {
        try {
            await deviceManager.UnregisterDeviceAsync(id);
            return Results.Accepted();
        }
        catch (Exception)
        {
            return Results.BadRequest();
        }
    }


    private static async Task<IResult> RegisterDeviceAsync(
        [FromQuery] string name,
        [FromServices] IDeviceManager deviceManager)
    {
        try {
            await deviceManager.RegisterDeviceAsync(name);
            return Results.Created();
        }
        catch (Exception)
        {
            return Results.BadRequest();
        }
    }

    private static async Task<IResult> GetDevicesAsync(
        [FromServices] IDeviceManager deviceManager)
    {
        try
        {
            var device = await deviceManager.GetDevices();
            return Results.Json(device);
        }
        catch (Exception)
        {
            return Results.BadRequest();
        }
    }
}