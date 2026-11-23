using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

/// <summary>
/// End-to-end encrypted data. The server checks ownership, sizes and formats, but has no key for the contents:
/// clients encrypt usage, apps and settings before sending them and decrypt them after loading.
/// </summary>
public static class E2EEndpoints
{
    public const int MaxEntriesPerUpload = 5000;

    /// <summary>One app's hour, encrypted: a handful of websites at most, so this is generous.</summary>
    public const int MaxUsageBlobBytes = 16 * 1024;

    /// <summary>App name, category and two icons.</summary>
    public const int MaxAppBlobBytes = 256 * 1024;

    public const int MaxSettingsBytes = 1024 * 1024;

    /// <summary>Longest range one usage query may cover.</summary>
    public static readonly TimeSpan MaxQueryRange = TimeSpan.FromDays(400);

    public static IEndpointRouteBuilder MapE2EEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/e2e")
            .WithTags("End-to-end encrypted data")
            .RequireAuthorization(Program.ApiPolicy);

        group.MapGet("/vault", GetVaultAsync).Produces<VaultDto>();
        group.MapPost("/vault", CreateVaultAsync);
        group.MapPut("/vault/key", UpdateKeyAsync);
        group.MapPut("/vault/settings", UpdateSettingsAsync).Produces<VaultSettingsVersionDto>();

        group.MapGet("/usage", GetUsageAsync).Produces<List<EncryptedUsageDto>>();
        group.MapPost("/usage/delete", DeleteUsageAsync);

        group.MapGet("/apps", GetAppsAsync).Produces<List<EncryptedAppDto>>();
        group.MapPut("/apps", PutAppsAsync);

        group.MapGet("/legacy", GetLegacyStatusAsync).Produces<LegacyStatusDto>();
        group.MapDelete("/legacy", DeleteLegacyAsync);

        routes.MapGroup("/api/devices")
            .RequireAuthorization(Program.ApiPolicy)
            .MapPost("/{deviceId:int}/e2e-usage", UploadUsageAsync)
            .Produces<EncryptedUsageUploadResultDto>();

        return routes;
    }

    public static async Task<IResult> GetVaultAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var vault = await db.UserVaults.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
        return vault is null
            ? Results.NotFound("Encryption has not been set up for this account yet.")
            : Results.Ok(new VaultDto(vault.Salt, vault.Iterations, vault.WrappedKey, vault.PublicKey,
                vault.EncryptedPrivateKey, vault.Settings, vault.SettingsVersion,
                DateTime.SpecifyKind(vault.CreatedAt, DateTimeKind.Utc)));
    }

    public static async Task<IResult> CreateVaultAsync([FromBody] CreateVaultDto dto, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        if (ValidateKey(dto.Salt, dto.Iterations, dto.WrappedKey) is { } keyError)
            return Results.BadRequest(keyError);
        if (dto.PublicKey is not { Length: 65 } || dto.PublicKey[0] != 0x04)
            return Results.BadRequest("The public key must be an uncompressed P-256 point.");
        if (dto.EncryptedPrivateKey is not { Length: > 0 and <= 256 })
            return Results.BadRequest("Invalid private key.");
        if (dto.Settings is not { Length: > 0 and <= MaxSettingsBytes })
            return Results.BadRequest("Invalid settings.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        if (await db.UserVaults.AnyAsync(v => v.UserId == userId))
            return Results.Conflict("Encryption is already set up for this account.");

        var now = time.GetUtcNow().UtcDateTime;
        db.UserVaults.Add(new UserVaultEntity
        {
            UserId = userId,
            Salt = dto.Salt,
            Iterations = dto.Iterations,
            WrappedKey = dto.WrappedKey,
            PublicKey = dto.PublicKey,
            EncryptedPrivateKey = dto.EncryptedPrivateKey,
            Settings = dto.Settings,
            SettingsVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two devices set up encryption at the same moment; the first one wins.
            return Results.Conflict("Encryption is already set up for this account.");
        }

        return Results.Created("/api/e2e/vault", null);
    }

    /// <summary>A new passphrase. The master key stays the same, so nothing has to be encrypted again.</summary>
    public static async Task<IResult> UpdateKeyAsync([FromBody] UpdateVaultKeyDto dto, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        if (ValidateKey(dto.Salt, dto.Iterations, dto.WrappedKey) is { } error)
            return Results.BadRequest(error);

        var userId = await currentUser.GetRequiredUserIdAsync();
        var updated = await db.UserVaults.Where(v => v.UserId == userId).ExecuteUpdateAsync(v => v
            .SetProperty(x => x.Salt, dto.Salt)
            .SetProperty(x => x.Iterations, dto.Iterations)
            .SetProperty(x => x.WrappedKey, dto.WrappedKey)
            .SetProperty(x => x.UpdatedAt, time.GetUtcNow().UtcDateTime));
        return updated == 0 ? Results.NotFound() : Results.NoContent();
    }

    public static async Task<IResult> UpdateSettingsAsync([FromBody] UpdateVaultSettingsDto dto,
        ApplicationDbContext db, CurrentUser currentUser, TimeProvider time)
    {
        if (dto.Settings is not { Length: > 0 and <= MaxSettingsBytes })
            return Results.BadRequest($"Settings must be at most {MaxSettingsBytes / 1024} KB.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        // Only replaces the version the client started from; a concurrent change makes this update nothing.
        var updated = await db.UserVaults
            .Where(v => v.UserId == userId && v.SettingsVersion == dto.ExpectedVersion)
            .ExecuteUpdateAsync(v => v
                .SetProperty(x => x.Settings, dto.Settings)
                .SetProperty(x => x.SettingsVersion, dto.ExpectedVersion + 1)
                .SetProperty(x => x.UpdatedAt, time.GetUtcNow().UtcDateTime));

        if (updated == 1)
            return Results.Ok(new VaultSettingsVersionDto(dto.ExpectedVersion + 1));

        return await db.UserVaults.AnyAsync(v => v.UserId == userId)
            ? Results.Conflict("The settings were changed on another device.")
            : Results.NotFound();
    }

    public static async Task<IResult> GetUsageAsync([FromQuery] DateTime start, [FromQuery] DateTime end,
        [FromQuery] int? deviceId, ApplicationDbContext db, CurrentUser currentUser)
    {
        start = ToUtc(start);
        end = ToUtc(end);
        if (end <= start || end - start > MaxQueryRange)
            return Results.BadRequest($"The range must be positive and at most {MaxQueryRange.TotalDays} days.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        var query = db.EncryptedUsages.AsNoTracking()
            .Where(u => u.Device!.UserId == userId && u.Hour >= start && u.Hour < end);
        if (deviceId is not null)
            query = query.Where(u => u.DeviceId == deviceId);

        var rows = await query
            .OrderBy(u => u.Hour)
            .Select(u => new EncryptedUsageDto(u.DeviceId, u.Tag, u.Hour, u.Data))
            .ToListAsync();

        // SQLite returns DateTimes as Unspecified; they are stored as UTC.
        return Results.Ok(rows.Select(r => r with { Hour = DateTime.SpecifyKind(r.Hour, DateTimeKind.Utc) }));
    }

    /// <summary>
    /// Stores encrypted rows for one device. A row replaces the stored row of the same app (tag) and hour, so
    /// collectors can send the still-growing current hour again, and retries never double count.
    /// </summary>
    public static async Task<IResult> UploadUsageAsync([FromRoute] int deviceId,
        [FromBody] EncryptedUsageUploadDto dto, ApplicationDbContext db, CurrentUser currentUser, TimeProvider time)
    {
        var entries = dto.Entries ?? [];
        if (entries.Count > MaxEntriesPerUpload)
            return Results.BadRequest($"At most {MaxEntriesPerUpload} entries per upload.");

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var entry in entries)
        {
            if (!IsTag(entry.Tag))
                return Results.BadRequest("Invalid tag.");
            var hour = ToUtc(entry.Hour);
            if (hour.Minute != 0 || hour.Second != 0 || hour.Millisecond != 0 || hour > now.AddHours(1))
                return Results.BadRequest("Hours must be whole UTC hours, not in the future.");
            if (entry.Data is not { Length: > 0 and <= MaxUsageBlobBytes })
                return Results.BadRequest("Invalid data.");
        }

        var userId = await currentUser.GetRequiredUserIdAsync();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);
        if (device is null)
            return Results.NotFound("Device not found");

        // The last entry wins when one upload contains an app's hour twice.
        var incoming = entries
            .GroupBy(e => (e.Tag, Hour: ToUtc(e.Hour)))
            .ToDictionary(g => g.Key, g => g.Last().Data);

        if (incoming.Count > 0)
        {
            var first = incoming.Keys.Min(k => k.Hour);
            var last = incoming.Keys.Max(k => k.Hour);
            var tags = incoming.Keys.Select(k => k.Tag).Distinct().ToList();
            var existing = await db.EncryptedUsages
                .Where(u => u.DeviceId == deviceId && u.Hour >= first && u.Hour <= last && tags.Contains(u.Tag))
                .ToListAsync();
            var byKey = existing.ToDictionary(u => (u.Tag, DateTime.SpecifyKind(u.Hour, DateTimeKind.Utc)));

            foreach (var ((tag, hour), data) in incoming)
            {
                if (byKey.TryGetValue((tag, hour), out var row))
                    row.Data = data;
                else
                    db.EncryptedUsages.Add(new EncryptedUsage { DeviceId = deviceId, Tag = tag, Hour = hour, Data = data });
            }
        }

        device.LastSeen = now;
        await db.SaveChangesAsync();

        var uploadedTags = incoming.Keys.Select(k => k.Tag).Distinct().ToList();
        var known = await db.EncryptedApps
            .Where(a => a.UserId == userId && uploadedTags.Contains(a.Tag))
            .Select(a => a.Tag)
            .ToListAsync();

        return Results.Ok(new EncryptedUsageUploadResultDto(incoming.Count, uploadedTags.Except(known).ToList()));
    }

    /// <summary>Deletes single rows, e.g. after the client moved their content to other rows.</summary>
    public static async Task<IResult> DeleteUsageAsync([FromBody] DeleteEncryptedUsageDto dto,
        ApplicationDbContext db, CurrentUser currentUser)
    {
        var rows = dto.Rows ?? [];
        if (rows.Count > MaxEntriesPerUpload)
            return Results.BadRequest($"At most {MaxEntriesPerUpload} rows per request.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        var deleted = 0;
        foreach (var device in rows.GroupBy(r => r.DeviceId))
        {
            var keys = device.Select(r => (r.Tag, Hour: ToUtc(r.Hour))).ToHashSet();
            var tags = keys.Select(k => k.Tag).Distinct().ToList();
            var first = keys.Min(k => k.Hour);
            var last = keys.Max(k => k.Hour);
            var candidates = await db.EncryptedUsages
                .Where(u => u.DeviceId == device.Key && u.Device!.UserId == userId)
                .Where(u => u.Hour >= first && u.Hour <= last && tags.Contains(u.Tag))
                .ToListAsync();
            var matching = candidates
                .Where(u => keys.Contains((u.Tag, DateTime.SpecifyKind(u.Hour, DateTimeKind.Utc))))
                .ToList();
            db.EncryptedUsages.RemoveRange(matching);
            deleted += matching.Count;
        }

        await db.SaveChangesAsync();
        return Results.Ok(deleted);
    }

    public static async Task<IResult> GetAppsAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        return Results.Ok(await db.EncryptedApps.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new EncryptedAppDto(a.Tag, a.Data))
            .ToListAsync());
    }

    /// <param name="createOnly">Collectors only add apps the server has nothing for; the dashboards replace.</param>
    public static async Task<IResult> PutAppsAsync([FromBody] List<EncryptedAppDto> apps,
        [FromQuery] bool createOnly, ApplicationDbContext db, CurrentUser currentUser, TimeProvider time)
    {
        if (apps.Count > 1000)
            return Results.BadRequest("At most 1000 apps per request.");
        if (apps.Any(a => !IsTag(a.Tag) || a.Data is not { Length: > 0 and <= MaxAppBlobBytes }))
            return Results.BadRequest($"Invalid app (tags as for usage, data at most {MaxAppBlobBytes / 1024} KB).");

        var userId = await currentUser.GetRequiredUserIdAsync();
        var tags = apps.Select(a => a.Tag).Distinct().ToList();
        var existing = await db.EncryptedApps
            .Where(a => a.UserId == userId && tags.Contains(a.Tag))
            .ToDictionaryAsync(a => a.Tag);
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var app in apps.GroupBy(a => a.Tag).Select(g => g.Last()))
        {
            if (existing.TryGetValue(app.Tag, out var row))
            {
                if (createOnly)
                    continue;
                row.Data = app.Data;
                row.UpdatedAt = now;
            }
            else
            {
                db.EncryptedApps.Add(new EncryptedApp { UserId = userId, Tag = app.Tag, Data = app.Data, UpdatedAt = now });
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (createOnly)
        {
            // Another device of the user stored the same app at the same moment.
        }

        return Results.NoContent();
    }

    /// <summary>Plain-text data from before encryption, which the client moves into encrypted rows.</summary>
    public static async Task<IResult> GetLegacyStatusAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var usage = db.Usages.Where(u => u.Device!.UserId == userId);
        var count = await usage.CountAsync();
        return Results.Ok(new LegacyStatusDto(
            count,
            count == 0 ? null : DateTime.SpecifyKind(await usage.MinAsync(u => u.Date), DateTimeKind.Utc),
            count == 0 ? null : DateTime.SpecifyKind(await usage.MaxAsync(u => u.Date), DateTimeKind.Utc),
            await usage.Select(u => u.AppId).Distinct().CountAsync(),
            await db.WebsiteExclusions.CountAsync(e => e.UserId == userId)));
    }

    /// <summary>
    /// Deletes the user's plain-text usage, app names and excluded websites, after the client stored them
    /// encrypted. Apps that no account uses any more are removed from the shared app list as well.
    /// </summary>
    public static async Task<IResult> DeleteLegacyAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        if (!await db.UserVaults.AnyAsync(v => v.UserId == userId))
            return Results.BadRequest("Set up encryption first; the data would be lost otherwise.");

        await db.Usages.Where(u => u.Device!.UserId == userId).ExecuteDeleteAsync();
        await db.AppOverrides.Where(o => o.UserId == userId).ExecuteDeleteAsync();
        await db.WebsiteExclusions.Where(e => e.UserId == userId).ExecuteDeleteAsync();
        await db.RegisteredApps
            .Where(a => !db.Usages.Any(u => u.AppId == a.Id) && !db.AppOverrides.Any(o => o.AppId == a.Id))
            .ExecuteDeleteAsync();
        return Results.NoContent();
    }

    private static string? ValidateKey(byte[]? salt, int iterations, byte[]? wrappedKey)
    {
        if (salt is not { Length: >= 16 and <= 64 })
            return "The salt must have 16 to 64 bytes.";
        if (iterations is < 100_000 or > 10_000_000)
            return "Iterations must be between 100,000 and 10,000,000.";
        if (wrappedKey is not { Length: > 0 and <= 256 })
            return "Invalid wrapped key.";
        return null;
    }

    /// <summary>Tags are 16 bytes in base64url: 22 characters.</summary>
    public static bool IsTag(string? tag) =>
        tag is { Length: 22 } && tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static DateTime ToUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc)
    };
}
