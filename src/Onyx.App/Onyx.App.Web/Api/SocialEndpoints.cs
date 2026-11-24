using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

/// <summary>
/// Friends, groups and the numbers users share with them. Shared numbers are encrypted by the sender for each
/// recipient; the server only checks that sender and recipient are connected, and relays them.
/// </summary>
public static class SocialEndpoints
{
    public const int MaxGroupNameLength = 100;
    /// <summary>The numbers plus a 64 px profile picture.</summary>
    public const int MaxShareBytes = 16 * 1024;

    /// <summary>Shares older than this are not returned any more (and are deleted when new ones arrive).</summary>
    public const int ShareWeeksKept = 12;

    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder routes)
    {
        var friends = routes.MapGroup("/api/friends")
            .WithTags("Friends")
            .RequireAuthorization(Program.ApiPolicy);
        friends.MapGet("/", GetFriendsAsync).Produces<List<FriendDto>>();
        // Rate limited: the answer tells whether a user name exists.
        friends.MapPost("/", RequestFriendAsync).RequireRateLimiting(Program.AuthRateLimit);
        friends.MapPost("/{id:int}/accept", AcceptFriendAsync);
        friends.MapDelete("/{id:int}", RemoveFriendAsync);

        var groups = routes.MapGroup("/api/groups")
            .WithTags("Groups")
            .RequireAuthorization(Program.ApiPolicy);
        groups.MapGet("/", GetGroupsAsync).Produces<List<GroupDto>>();
        groups.MapPost("/", CreateGroupAsync).Produces<GroupDto>();
        groups.MapPost("/{id:int}/invite", InviteAsync).RequireRateLimiting(Program.AuthRateLimit);
        groups.MapPost("/{id:int}/accept", AcceptInvitationAsync);
        groups.MapDelete("/{id:int}/members/{userId}", RemoveMemberAsync);
        groups.MapDelete("/{id:int}", DeleteGroupAsync);

        var shares = routes.MapGroup("/api/shares")
            .WithTags("Shared numbers")
            .RequireAuthorization(Program.ApiPolicy);
        shares.MapGet("/recipients", GetRecipientsAsync).Produces<List<ShareRecipientDto>>();
        shares.MapPut("/", PutSharesAsync);
        shares.MapGet("/", GetReceivedAsync).Produces<List<ReceivedShareDto>>();

        return routes;
    }

    // Friends

    public static async Task<IResult> GetFriendsAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        return Results.Ok(await FriendsOfAsync(db, userId));
    }

    internal static async Task<List<FriendDto>> FriendsOfAsync(ApplicationDbContext db, string userId)
    {
        var rows = await db.Friendships.AsNoTracking()
            .Where(f => f.RequesterId == userId || f.AddresseeId == userId)
            .Select(f => new
            {
                f.Id,
                f.Accepted,
                f.CreatedAt,
                f.AcceptedAt,
                Incoming = f.AddresseeId == userId,
                Other = f.AddresseeId == userId ? f.Requester! : f.Addressee!
            })
            .ToListAsync();

        return rows
            .Select(f => new FriendDto(f.Id, f.Other.Id, f.Other.UserName ?? "", AvatarUrl(f.Other), f.Incoming,
                f.Accepted, DateTime.SpecifyKind(f.AcceptedAt ?? f.CreatedAt, DateTimeKind.Utc)))
            .OrderBy(f => f.Accepted)
            .ThenBy(f => f.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<IResult> RequestFriendAsync([FromBody] FriendRequestDto dto, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        if (await FindUserAsync(db, dto.UserNameOrEmail) is not { } other)
            return Results.NotFound("There is no user with that name or email address on this server.");
        if (other.Id == userId)
            return Results.BadRequest("You can't add yourself.");

        var existing = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == other.Id) ||
            (f.RequesterId == other.Id && f.AddresseeId == userId));

        if (existing is not null)
        {
            // They asked first: asking back accepts.
            if (!existing.Accepted && existing.AddresseeId == userId)
            {
                existing.Accepted = true;
                existing.AcceptedAt = time.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync();
                return Results.Ok();
            }

            return Results.Conflict(existing.Accepted
                ? "You are friends already."
                : "You have already sent a request.");
        }

        db.Friendships.Add(new Friendship
        {
            RequesterId = userId,
            AddresseeId = other.Id,
            CreatedAt = time.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    public static async Task<IResult> AcceptFriendAsync([FromRoute] int id, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var request = await db.Friendships.FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == userId);
        if (request is null)
            return Results.NotFound();

        if (!request.Accepted)
        {
            request.Accepted = true;
            request.AcceptedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        return Results.Ok();
    }

    /// <summary>Declines a request, withdraws one, or ends a friendship. Numbers shared between the two are
    /// deleted unless they still share a group.</summary>
    public static async Task<IResult> RemoveFriendAsync([FromRoute] int id, ApplicationDbContext db,
        CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var friendship = await db.Friendships.FirstOrDefaultAsync(f =>
            f.Id == id && (f.RequesterId == userId || f.AddresseeId == userId));
        if (friendship is null)
            return Results.NotFound();

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync();
        await DeleteSharesNoLongerAllowedAsync(db, friendship.RequesterId, friendship.AddresseeId);
        return Results.NoContent();
    }

    // Groups

    public static async Task<IResult> GetGroupsAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        return Results.Ok(await GroupsOfAsync(db, userId));
    }

    internal static async Task<List<GroupDto>> GroupsOfAsync(ApplicationDbContext db, string userId)
    {
        var groupIds = await db.GroupHasUsers.Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync();
        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync();
        var members = await db.GroupHasUsers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId))
            .Select(m => new { m.GroupId, m.Accepted, User = m.User! })
            .ToListAsync();

        return groups
            .Select(g =>
            {
                var own = members.First(m => m.GroupId == g.Id && m.User.Id == userId);
                return new GroupDto(g.Id, g.Name ?? "", g.AdminId == userId, own.Accepted, members
                    .Where(m => m.GroupId == g.Id)
                    // Invited users only see the other members once they accept.
                    .Where(m => own.Accepted || m.Accepted || m.User.Id == userId)
                    .Select(m => new GroupMemberDto(m.User.Id, m.User.UserName ?? "", AvatarUrl(m.User), m.Accepted,
                        m.User.Id == g.AdminId))
                    .OrderByDescending(m => m.IsAdmin)
                    .ThenBy(m => m.UserName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList());
            })
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<IResult> CreateGroupAsync([FromBody] CreateGroupDto dto, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        var name = dto.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxGroupNameLength)
            return Results.BadRequest($"A group name has 1 to {MaxGroupNameLength} characters.");

        var userId = await currentUser.GetRequiredUserIdAsync();
        var group = new Groups { Name = name, AdminId = userId };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.GroupHasUsers.Add(new GroupHasUser
            { GroupId = group.Id, UserId = userId, Accepted = true, InvitedAt = time.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();

        return Results.Ok((await GroupsOfAsync(db, userId)).First(g => g.Id == group.Id));
    }

    /// <summary>The group's admin invites someone; they become a member when they accept.</summary>
    public static async Task<IResult> InviteAsync([FromRoute] int id, [FromBody] GroupInviteDto dto,
        ApplicationDbContext db, CurrentUser currentUser, TimeProvider time)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        if (!await db.Groups.AnyAsync(g => g.Id == id && g.AdminId == userId))
            return Results.NotFound("Only the group's admin can invite.");
        if (await FindUserAsync(db, dto.UserNameOrEmail) is not { } other)
            return Results.NotFound("There is no user with that name or email address on this server.");
        if (await db.GroupHasUsers.AnyAsync(m => m.GroupId == id && m.UserId == other.Id))
            return Results.Conflict("Already a member or invited.");

        db.GroupHasUsers.Add(new GroupHasUser
            { GroupId = id, UserId = other.Id, Accepted = false, InvitedAt = time.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    public static async Task<IResult> AcceptInvitationAsync([FromRoute] int id, ApplicationDbContext db,
        CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var membership = await db.GroupHasUsers.FirstOrDefaultAsync(m => m.GroupId == id && m.UserId == userId);
        if (membership is null)
            return Results.NotFound();
        membership.Accepted = true;
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    /// <summary>Leave a group (or decline an invitation), or, as its admin, remove a member. When the admin
    /// leaves, the group is deleted.</summary>
    public static async Task<IResult> RemoveMemberAsync([FromRoute] int id, [FromRoute] string userId,
        ApplicationDbContext db, CurrentUser currentUser)
    {
        var me = await currentUser.GetRequiredUserIdAsync();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null || (userId != me && group.AdminId != me))
            return Results.NotFound();

        if (userId == group.AdminId)
            return await DeleteGroupAsync(id, db, currentUser);

        var membership = await db.GroupHasUsers.FirstOrDefaultAsync(m => m.GroupId == id && m.UserId == userId);
        if (membership is null)
            return Results.NotFound();

        var others = await db.GroupHasUsers.Where(m => m.GroupId == id && m.UserId != userId)
            .Select(m => m.UserId!).ToListAsync();
        db.GroupHasUsers.Remove(membership);
        await db.SaveChangesAsync();
        foreach (var other in others)
            await DeleteSharesNoLongerAllowedAsync(db, userId, other);
        return Results.NoContent();
    }

    public static async Task<IResult> DeleteGroupAsync([FromRoute] int id, ApplicationDbContext db,
        CurrentUser currentUser)
    {
        var me = await currentUser.GetRequiredUserIdAsync();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id && g.AdminId == me);
        if (group is null)
            return Results.NotFound();

        var members = await db.GroupHasUsers.Where(m => m.GroupId == id).Select(m => m.UserId!).ToListAsync();
        await db.GroupHasUsers.Where(m => m.GroupId == id).ExecuteDeleteAsync();
        db.Groups.Remove(group);
        await db.SaveChangesAsync();

        foreach (var a in members)
        foreach (var b in members.Where(b => string.CompareOrdinal(a, b) < 0))
            await DeleteSharesNoLongerAllowedAsync(db, a, b);
        return Results.NoContent();
    }

    // Shared numbers

    /// <summary>Everyone the user shares with: accepted friends and the accepted members of groups the user has
    /// joined, who have set up encryption (they need a public key).</summary>
    public static async Task<IResult> GetRecipientsAsync(ApplicationDbContext db, CurrentUser currentUser)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var ids = await PartnersOfAsync(db, userId);
        return Results.Ok(await db.UserVaults.AsNoTracking()
            .Where(v => ids.Contains(v.UserId))
            .Select(v => new ShareRecipientDto(v.UserId, v.User!.UserName ?? "", v.PublicKey))
            .ToListAsync());
    }

    public static async Task<IResult> PutSharesAsync([FromBody] ShareUploadDto dto, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        var items = dto.Items ?? [];
        if (items.Count > 1000 || items.Any(i => i.Data is not { Length: > 0 and <= MaxShareBytes }
                                                  || i.PeriodStart.DayOfWeek != DayOfWeek.Monday))
            return Results.BadRequest("Invalid shares (at most 1000, periods start on a Monday).");

        var userId = await currentUser.GetRequiredUserIdAsync();
        var partners = await PartnersOfAsync(db, userId);
        if (items.Any(i => !partners.Contains(i.RecipientId)))
            return Results.BadRequest("You can only share with friends and members of your groups.");

        var now = time.GetUtcNow().UtcDateTime;
        var periods = items.Select(i => i.PeriodStart).Distinct().ToList();
        var existing = await db.SharedStats
            .Where(s => s.OwnerId == userId && periods.Contains(s.PeriodStart))
            .ToDictionaryAsync(s => (s.RecipientId, s.PeriodStart));

        foreach (var item in items)
        {
            if (existing.TryGetValue((item.RecipientId, item.PeriodStart), out var row))
            {
                row.Data = item.Data;
                row.UpdatedAt = now;
            }
            else
            {
                var stat = new SharedStat
                {
                    OwnerId = userId, RecipientId = item.RecipientId, PeriodStart = item.PeriodStart,
                    Data = item.Data, UpdatedAt = now
                };
                db.SharedStats.Add(stat);
                existing[(item.RecipientId, item.PeriodStart)] = stat;
            }
        }

        await db.SaveChangesAsync();

        var oldest = DateOnly.FromDateTime(now).AddDays(-7 * ShareWeeksKept);
        await db.SharedStats.Where(s => s.OwnerId == userId && s.PeriodStart < oldest).ExecuteDeleteAsync();
        return Results.NoContent();
    }

    /// <summary>Numbers shared with the user by people they are still connected to.</summary>
    public static async Task<IResult> GetReceivedAsync([FromQuery] DateOnly? since, ApplicationDbContext db,
        CurrentUser currentUser, TimeProvider time)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var partners = await PartnersOfAsync(db, userId);
        var from = since ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-7 * ShareWeeksKept);

        var rows = await db.SharedStats.AsNoTracking()
            .Where(s => s.RecipientId == userId && s.PeriodStart >= from && partners.Contains(s.OwnerId))
            .Select(s => new
            {
                s.OwnerId, Owner = s.Owner!, s.PeriodStart, s.Data, s.UpdatedAt,
                PublicKey = db.UserVaults.Where(v => v.UserId == s.OwnerId).Select(v => v.PublicKey).FirstOrDefault()
            })
            .ToListAsync();

        return Results.Ok(rows
            .Where(r => r.PublicKey is not null)
            .Select(r => new ReceivedShareDto(r.OwnerId, r.Owner.UserName ?? "", AvatarUrl(r.Owner), r.PublicKey!,
                r.PeriodStart, r.Data, DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc)))
            .ToList());
    }

    /// <summary>Users the given user may share with (accepted friends, accepted fellow group members).</summary>
    internal static async Task<HashSet<string>> PartnersOfAsync(ApplicationDbContext db, string userId)
    {
        var friends = await db.Friendships
            .Where(f => f.Accepted && (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();

        var groups = db.GroupHasUsers.Where(m => m.UserId == userId && m.Accepted).Select(m => m.GroupId);
        var members = await db.GroupHasUsers
            .Where(m => groups.Contains(m.GroupId) && m.Accepted && m.UserId != userId)
            .Select(m => m.UserId!)
            .ToListAsync();

        return [.. friends, .. members];
    }

    private static async Task DeleteSharesNoLongerAllowedAsync(ApplicationDbContext db, string a, string b)
    {
        if ((await PartnersOfAsync(db, a)).Contains(b))
            return;
        await db.SharedStats
            .Where(s => (s.OwnerId == a && s.RecipientId == b) || (s.OwnerId == b && s.RecipientId == a))
            .ExecuteDeleteAsync();
    }

    private static async Task<ApplicationUser?> FindUserAsync(ApplicationDbContext db, string? nameOrEmail)
    {
        var value = nameOrEmail?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value))
            return null;
        return await db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == value || u.NormalizedEmail == value);
    }

    internal static string? AvatarUrl(ApplicationUser user) =>
        user.Avatar is { Length: > 0 } avatar
            ? $"data:{user.AvatarContentType ?? "image/png"};base64,{Convert.ToBase64String(avatar)}"
            : null;
}
