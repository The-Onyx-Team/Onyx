using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Goals;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Social;

/// <summary>What one user shares with friends for one week (encrypted for each of them).</summary>
public sealed record SharedWeek(
    [property: JsonPropertyName("w")] DateOnly WeekStart,
    [property: JsonPropertyName("l")] ShareLevel Level,
    [property: JsonPropertyName("t")] int TotalMinutes,
    [property: JsonPropertyName("d")] int[] DailyMinutes,
    [property: JsonPropertyName("c")] Dictionary<string, int>? CategoryMinutes,
    [property: JsonPropertyName("g")] List<SharedGoal>? Goals,
    [property: JsonPropertyName("n")] string? DisplayName = null,
    [property: JsonPropertyName("a")] string? Avatar = null);

public sealed record SharedGoal(
    [property: JsonPropertyName("t")] string Title,
    [property: JsonPropertyName("s")] int Streak,
    [property: JsonPropertyName("o")] bool OnTrack);

/// <summary>A person's numbers for the comparison (the user themselves included).</summary>
/// <param name="DisplayName">Their real name, if they share it (encrypted, with the numbers).</param>
public sealed record PersonWeeks(
    string UserId,
    string UserName,
    string? AvatarDataUrl,
    bool IsMe,
    IReadOnlyDictionary<DateOnly, SharedWeek> Weeks,
    DateTime? UpdatedAt,
    string? SafetyCode,
    string? DisplayName = null);

/// <summary>Friends, groups and the numbers shared with them.</summary>
public sealed class SocialService(
    OnyxApiClient api,
    VaultService vault,
    IUsageDataService usage,
    GoalService goals,
    AuthenticationStateProvider authState,
    PrivateProfileService profile,
    ILogger<SocialService> logger)
{
    public Task<List<FriendDto>> GetFriendsAsync() =>
        api.SendAuthorizedAsync<List<FriendDto>>(HttpMethod.Get, "/api/friends");

    /// <returns>Null on success, otherwise a message for the user.</returns>
    public Task<string?> RequestFriendAsync(string userNameOrEmail) =>
        TryAsync(HttpMethod.Post, "/api/friends", new FriendRequestDto(userNameOrEmail.Trim()));

    public Task<string?> AcceptFriendAsync(int id) => TryAsync(HttpMethod.Post, $"/api/friends/{id}/accept");

    public Task<string?> RemoveFriendAsync(int id) => TryAsync(HttpMethod.Delete, $"/api/friends/{id}");

    public Task<List<GroupDto>> GetGroupsAsync() => api.SendAuthorizedAsync<List<GroupDto>>(HttpMethod.Get, "/api/groups");

    public Task<string?> CreateGroupAsync(string name) => TryAsync(HttpMethod.Post, "/api/groups", new CreateGroupDto(name.Trim()));

    public Task<string?> InviteAsync(int groupId, string userNameOrEmail) =>
        TryAsync(HttpMethod.Post, $"/api/groups/{groupId}/invite", new GroupInviteDto(userNameOrEmail.Trim()));

    public Task<string?> AcceptInvitationAsync(int groupId) => TryAsync(HttpMethod.Post, $"/api/groups/{groupId}/accept");

    public Task<string?> RemoveMemberAsync(int groupId, string userId) =>
        TryAsync(HttpMethod.Delete, $"/api/groups/{groupId}/members/{Uri.EscapeDataString(userId)}");

    public Task<string?> DeleteGroupAsync(int groupId) => TryAsync(HttpMethod.Delete, $"/api/groups/{groupId}");

    /// <summary>Computes this and last week's numbers and sends them, encrypted, to every friend and group
    /// member. Called whenever the user opens the dashboard or the friends page.</summary>
    /// <returns>Number of people it was sent to.</returns>
    public async Task<int> PublishAsync(TimeZoneInfo zone, DateOnly today)
    {
        var recipients = await api.SendAuthorizedAsync<List<ShareRecipientDto>>(HttpMethod.Get, "/api/shares/recipients");
        if (recipients.Count == 0)
            return 0;

        var me = await MyIdAsync();
        var (privateKey, publicKey) = await vault.GetKeyPairAsync();
        var v = await vault.GetVaultAsync();
        var weeks = await ComputeMyWeeksAsync(zone, today);

        // Real name and picture travel with this week's numbers, encrypted like them (not when sharing nothing).
        if (weeks[0].Level != ShareLevel.Nothing)
        {
            try
            {
                var mine = await profile.GetAsync();
                weeks[0] = weeks[0] with { DisplayName = mine.DisplayName, Avatar = mine.ThumbnailDataUrl };
            }
            catch (Exception e) when (e is HttpRequestException or NotSignedInException)
            {
                logger.LogWarning(e, "Could not read the profile to share");
            }
        }

        var items = new List<ShareItemDto>();
        foreach (var recipient in recipients)
        foreach (var week in weeks)
        {
            items.Add(new ShareItemDto(recipient.UserId, week.WeekStart, await v.EncryptForAsync(week, privateKey,
                publicKey, recipient.PublicKey, E2EContext.Share(me, recipient.UserId, week.WeekStart))));
        }

        await api.SendAuthorizedAsync<object?>(HttpMethod.Put, "/api/shares", new ShareUploadDto(items));
        return recipients.Count;
    }

    /// <summary>The user's own numbers for this and last week, at their sharing level.</summary>
    public async Task<List<SharedWeek>> ComputeMyWeeksAsync(TimeZoneInfo zone, DateOnly today)
    {
        var settings = await vault.GetSettingsAsync();
        var level = settings.Sharing.Level;
        var thisWeek = Weeks.StartOf(today);
        var lastWeek = thisWeek.AddDays(-7);

        var rows = await usage.GetUsageDataAsync(
            TimeZoneInfo.ConvertTimeToUtc(lastWeek.ToDateTime(TimeOnly.MinValue), zone),
            TimeZoneInfo.ConvertTimeToUtc(thisWeek.AddDays(7).ToDateTime(TimeOnly.MinValue), zone));
        var local = rows.Select(r => (Row: r, Day: DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.Date, DateTimeKind.Utc), zone)))).ToList();

        List<SharedGoal>? sharedGoals = null;
        if (level == ShareLevel.CategoriesAndGoals)
        {
            sharedGoals = (await goals.EvaluateAsync(zone, today))
                .Select(g => new SharedGoal(g.Title, g.Streak, g.OnTrack))
                .ToList();
        }

        return [Week(thisWeek), Week(lastWeek)];

        SharedWeek Week(DateOnly start)
        {
            if (level == ShareLevel.Nothing)
                return new SharedWeek(start, level, 0, [], null, null);

            var inWeek = local.Where(x => x.Day >= start && x.Day < start.AddDays(7)).ToList();
            int Minutes(IEnumerable<(UsageDto Row, DateOnly Day)> items) =>
                (int)Math.Round(items.Sum(x => x.Row.TimeSpan.TotalMinutes));

            return new SharedWeek(
                start,
                level,
                Minutes(inWeek),
                Enumerable.Range(0, 7).Select(i => Minutes(inWeek.Where(x => x.Day == start.AddDays(i)))).ToArray(),
                level >= ShareLevel.Categories
                    ? inWeek.GroupBy(x => x.Row.Category ?? "Uncategorized").ToDictionary(g => g.Key, g => Minutes(g))
                    : null,
                start == Weeks.StartOf(today) ? sharedGoals : null);
        }
    }

    /// <summary>Everyone's numbers for the comparison: the user first, then the people sharing with them.</summary>
    public async Task<List<PersonWeeks>> GetComparisonAsync(TimeZoneInfo zone, DateOnly today, string myName)
    {
        var me = await MyIdAsync();
        var (privateKey, publicKey) = await vault.GetKeyPairAsync();
        var v = await vault.GetVaultAsync();

        var people = new List<PersonWeeks>
        {
            new(me, myName, null, true, (await ComputeMyWeeksAsync(zone, today)).ToDictionary(w => w.WeekStart), null, null)
        };

        var received = await api.SendAuthorizedAsync<List<ReceivedShareDto>>(HttpMethod.Get, "/api/shares");
        foreach (var owner in received.GroupBy(r => r.OwnerId))
        {
            var weeks = new Dictionary<DateOnly, SharedWeek>();
            foreach (var share in owner)
            {
                var week = await v.DecryptFromAsync<SharedWeek>(share.Data, privateKey, publicKey, share.OwnerPublicKey,
                    E2EContext.Share(share.OwnerId, me, share.PeriodStart));
                if (week is not null && week.WeekStart == share.PeriodStart)
                    weeks[week.WeekStart] = week;
                else
                    logger.LogWarning("Could not decrypt what {Owner} shared for {Week}", share.OwnerUserName, share.PeriodStart);
            }

            var first = owner.First();
            var latest = weeks.Values.OrderByDescending(w => w.WeekStart)
                .FirstOrDefault(w => w.DisplayName is not null || w.Avatar is not null);
            people.Add(new PersonWeeks(owner.Key, first.OwnerUserName,
                ValidAvatar(latest?.Avatar) ?? first.OwnerAvatarDataUrl, false, weeks,
                owner.Max(o => o.UpdatedAt), UserVault.SafetyCode(publicKey, first.OwnerPublicKey), latest?.DisplayName));
        }

        return people;
    }

    /// <summary>Safety codes with everyone the user shares with, to compare in person.</summary>
    public async Task<Dictionary<string, string>> GetSafetyCodesAsync()
    {
        var (_, publicKey) = await vault.GetKeyPairAsync();
        var recipients = await api.SendAuthorizedAsync<List<ShareRecipientDto>>(HttpMethod.Get, "/api/shares/recipients");
        return recipients.ToDictionary(r => r.UserId, r => UserVault.SafetyCode(publicKey, r.PublicKey));
    }

    /// <summary>Only small JPEG data URLs are shown, whatever a friend's app sends.</summary>
    private static string? ValidAvatar(string? url) =>
        url is { Length: < 16 * 1024 } && url.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) ? url : null;

    private async Task<string> MyIdAsync() =>
        (await authState.GetAuthenticationStateAsync()).User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new NotSignedInException("Not signed in");

    private async Task<string?> TryAsync(HttpMethod method, string path, object? body = null)
    {
        try
        {
            await api.SendAuthorizedAsync<object?>(method, path, body);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException)
        {
            return OnyxApiClient.ErrorMessage(e);
        }
    }
}
