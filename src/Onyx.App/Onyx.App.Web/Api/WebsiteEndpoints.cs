using Microsoft.AspNetCore.Mvc;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Api;

public static class WebsiteEndpoints
{
    public static RouteGroupBuilder MapWebsiteEndpoints(this IEndpointRouteBuilder routes)
    {
        // Settings for website recording. The websites themselves arrive with the usage uploads.
        var group = routes.MapGroup("/api/websites")
            .WithTags("Websites API")
            .RequireAuthorization(Program.ApiPolicy);

        group.MapGet("/exclusions", GetExclusionsAsync).Produces<WebsiteExclusionsDto>();
        group.MapPut("/exclusions", SetExclusionsAsync).Produces<WebsiteExclusionsDto>();
        group.MapDelete("/history", DeleteHistoryAsync);

        return group;
    }

    public static async Task<IResult> GetExclusionsAsync([FromServices] Services.Data.WebsiteSettings settings) =>
        Results.Ok(new WebsiteExclusionsDto(await settings.GetExcludedDomainsAsync()));

    public static async Task<IResult> SetExclusionsAsync(
        [FromBody] WebsiteExclusionsDto dto,
        [FromServices] Services.Data.WebsiteSettings settings)
    {
        try
        {
            return Results.Ok(new WebsiteExclusionsDto(await settings.SetExcludedDomainsAsync(dto.Domains ?? [])));
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(e.Message);
        }
    }

    public static async Task<IResult> DeleteHistoryAsync([FromServices] Services.Data.WebsiteSettings settings)
    {
        await settings.DeleteWebsiteHistoryAsync();
        return Results.NoContent();
    }
}
