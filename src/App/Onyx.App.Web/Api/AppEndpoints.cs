namespace Onyx.App.Web.Api;

public static class AppEndpoints
{
    public static RouteGroupBuilder MapAppEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/app");

        group.MapPost("/", RegisterAppAsync);
        group.MapGet("/", GetAppsAsync);

        return group;
    }

    private static async Task<IResult> RegisterAppAsync(
        [FromBody] AppDto app,
        [FromServices] IAppManager appManager)
    {
        var id = await appManager.RegisterAppAsync(app.Name, app.IconBitmap);
        return TypedResults.Ok(id);
    }

    private static async Task<IResult> GetAppsAsync([FromServices] IAppManager appManager) => 
        Results.Ok(await appManager.GetAppsAsync());
}