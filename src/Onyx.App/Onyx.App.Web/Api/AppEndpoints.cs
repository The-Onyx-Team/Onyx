using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Api;

public static class AppEndpoints
{
    public static RouteGroupBuilder MapAppEndpoints(this IEndpointRouteBuilder routes)
    {
        // Apps are created implicitly by usage uploads; clients only list them and fill in missing icons.
        var group = routes.MapGroup("/api/app")
            .WithTags("Apps API")
            .RequireAuthorization(Program.ApiPolicy);

        group.MapGet("/", GetAppsAsync).Produces<List<AppDto>>();
        group.MapPut("/icon", SetIconAsync);
        group.MapPut("/{appId:int}/override", SetOverrideAsync);

        return group;
    }

    public static async Task<IResult> GetAppsAsync([FromServices] IAppManager appManager) =>
        Results.Ok(await appManager.GetAppsAsync());

    public static async Task<IResult> SetIconAsync(
        [FromBody] AppIconDto dto,
        [FromServices] IAppManager appManager)
    {
        try
        {
            return await appManager.SetIconAsync(dto.AppIdentifier, dto.IconPng)
                ? Results.NoContent()
                : Results.Conflict("Unknown app or icon already set");
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(e.Message);
        }
    }

    public static async Task<IResult> SetOverrideAsync(
        int appId,
        [FromBody] AppOverrideDto dto,
        [FromServices] IAppManager appManager)
    {
        try
        {
            return await appManager.SetOverrideAsync(appId, dto.DisplayName, dto.Category)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(e.Message);
        }
    }
}
