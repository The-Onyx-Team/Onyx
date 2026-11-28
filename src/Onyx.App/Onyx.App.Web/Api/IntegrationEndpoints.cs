using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Onyx.App.Web.Services.Auth;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

/// <summary>
/// Integrations: read-only shares of a user's numbers for other programs (see <see cref="IntegrationKey"/>). The
/// user's devices register them and upload encrypted snapshots; the integration downloads its snapshot with its
/// access token and decrypts it itself.
/// </summary>
public static class IntegrationEndpoints
{
    public const int MaxIntegrations = 20;

    /// <summary>A year of daily numbers with apps, categories, websites and devices fits comfortably.</summary>
    public const int MaxSnapshotBytes = 4 * 1024 * 1024;

    public const string RateLimit = "integration";

    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/integrations")
            .WithTags("Integrations API")
            .RequireAuthorization(Program.ApiPolicy);
        group.MapGet("/", ListAsync).Produces<List<IntegrationDto>>();
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}/data", PutDataAsync);
        group.MapDelete("/{id}", DeleteAsync);

        // Used by the integration itself, with its access token instead of a user's sign-in.
        routes.MapGet("/api/integration/data", GetDataAsync)
            .WithTags("Integrations API")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimit)
            .Produces<IntegrationExportDto>();

        return routes;
    }

    public static async Task<IResult> ListAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        return Results.Ok(await db.Integrations.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new IntegrationDto(i.Id, i.CreatedAt, i.LastUsedAt, i.DataUpdatedAt, i.Data == null ? 0 : i.Data.Length))
            .ToListAsync());
    }

    public static async Task<IResult> CreateAsync([FromBody] CreateIntegrationDto dto, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        if (dto.Id is not { Length: IntegrationKey.IdLength } || !IntegrationKey.IsBase64Url(dto.Id)
            || dto.AccessToken is not { Length: >= 40 and <= 64 })
            return Results.BadRequest("Invalid integration id or token.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        if (await db.Integrations.CountAsync(i => i.UserId == userId) >= MaxIntegrations)
            return Results.BadRequest($"At most {MaxIntegrations} integrations.");
        if (await db.Integrations.AnyAsync(i => i.Id == dto.Id))
            return Results.Conflict();

        db.Integrations.Add(new Integration
        {
            Id = dto.Id, UserId = userId, TokenHash = IntegrationKey.HashToken(dto.AccessToken),
            CreatedAt = time.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
        return Results.Created($"/api/integrations/{dto.Id}", null);
    }

    public static async Task<IResult> PutDataAsync([FromRoute] string id, [FromBody] IntegrationDataDto dto,
        ApplicationDbContext db, CurrentUser currentUser, TimeProvider time)
    {
        if (dto.Data is not { Length: > CryptoFormat.Overhead and <= MaxSnapshotBytes })
            return Results.BadRequest($"The snapshot must be at most {MaxSnapshotBytes / 1024 / 1024} MB.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        var updated = await db.Integrations.Where(i => i.Id == id && i.UserId == userId).ExecuteUpdateAsync(i => i
            .SetProperty(x => x.Data, dto.Data)
            .SetProperty(x => x.DataUpdatedAt, time.GetUtcNow().UtcDateTime));
        return updated == 0 ? Results.NotFound() : Results.NoContent();
    }

    public static async Task<IResult> DeleteAsync([FromRoute] string id, ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var deleted = await db.Integrations.Where(i => i.Id == id && i.UserId == userId).ExecuteDeleteAsync();
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    /// <summary>The snapshot for the integration whose token is in <c>Authorization: Bearer &lt;id&gt;.&lt;token&gt;</c>.</summary>
    public static async Task<IResult> GetDataAsync(HttpRequest request, ApplicationDbContext db, TimeProvider time)
    {
        var header = request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        var credential = header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? header[scheme.Length..].Trim() : "";
        var dot = credential.IndexOf('.');
        if (dot != IntegrationKey.IdLength)
            return Results.Unauthorized();

        var id = credential[..dot];
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (integration is null
            || !CryptographicOperations.FixedTimeEquals(integration.TokenHash, IntegrationKey.HashToken(credential[(dot + 1)..])))
            return Results.Unauthorized();

        var now = time.GetUtcNow().UtcDateTime;
        await db.Integrations.Where(i => i.Id == id).ExecuteUpdateAsync(i => i.SetProperty(x => x.LastUsedAt, now));
        return Results.Ok(new IntegrationExportDto(integration.Id, integration.DataUpdatedAt, integration.Data));
    }
}
