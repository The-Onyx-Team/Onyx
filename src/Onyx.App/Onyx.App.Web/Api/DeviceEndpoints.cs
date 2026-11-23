using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Api;

public static class DeviceEndpoints
{
    public const string LegacyUploadRefused =
        "This account encrypts its data end to end. Update the Onyx app or agent to upload.";

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var devices = endpoints.MapGroup("/api/devices")
            .WithTags("Devices API")
            .RequireAuthorization(Program.ApiPolicy);

        devices.MapGet("/", GetDevicesAsync).Produces<List<DeviceDto>>();
        devices.MapPost("/", RegisterDeviceAsync).Produces<DeviceDto>(StatusCodes.Status201Created);
        devices.MapDelete("/{deviceId:int}", UnregisterDeviceAsync);
        devices.MapPost("/{deviceId:int}/usage", UploadUsageAsync).Produces<UsageUploadResultDto>();

        return endpoints;
    }

    public static async Task<IResult> GetDevicesAsync([FromServices] IDeviceManager deviceManager) =>
        Results.Ok(await deviceManager.GetDevices());

    public static async Task<IResult> RegisterDeviceAsync(
        [FromBody] CreateDeviceDto dto,
        [FromServices] IDeviceManager deviceManager)
    {
        try
        {
            var device = await deviceManager.RegisterDeviceAsync(dto.Name, dto.Platform);
            return Results.Created($"/api/devices/{device.Id}", device);
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(e.Message);
        }
    }

    public static async Task<IResult> UnregisterDeviceAsync(
        [FromRoute] int deviceId,
        [FromServices] IDeviceManager deviceManager)
    {
        try
        {
            await deviceManager.UnregisterDeviceAsync(deviceId);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    /// <summary>
    /// Stores hourly usage buckets for one device. Buckets that already exist are replaced, so collectors can
    /// re-send the current (still growing) hour on every sync and retry failed uploads without double counting.
    /// </summary>
    public static async Task<IResult> UploadUsageAsync(
        [FromRoute] int deviceId,
        [FromBody] UsageUploadDto dto,
        [FromServices] Services.Data.UsageDataService usageDataService,
        [FromServices] Onyx.Data.DataBaseSchema.ApplicationDbContext db,
        [FromServices] Services.Auth.CurrentUser currentUser)
    {
        // Plain-text uploads from app versions before end-to-end encryption. Once the account has encryption set
        // up they would mix readable data into the encrypted account, so they are refused; the old app keeps the
        // data and the updated one uploads it encrypted.
        var userId = await currentUser.GetRequiredUserIdAsync();
        if (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.UserVaults, v => v.UserId == userId))
            return Results.Problem(LegacyUploadRefused, statusCode: StatusCodes.Status409Conflict);

        try
        {
            return Results.Ok(await usageDataService.UploadUsageAsync(deviceId, dto.Entries ?? []));
        }
        catch (KeyNotFoundException)
        {
            // The collector should register itself again; the device was deleted from the dashboard.
            return Results.NotFound("Device not found");
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(e.Message);
        }
    }
}
