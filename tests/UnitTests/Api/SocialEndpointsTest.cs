using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Moq;
using Onyx.App.Web.Api;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

[TestSubject(typeof(SocialEndpoints))]
public class SocialEndpointsTest : IDisposable
{
    private readonly TestDatabase m_Test = new();
    private ApplicationDbContext Db => m_Test.Db;
    private static readonly TimeProvider s_Time = TimeProvider.System;
    private static readonly DateOnly s_Week = new(2026, 11, 16);

    public SocialEndpointsTest()
    {
        foreach (var user in new[] { "alice", "bob", "carol" })
            Db.UserVaults.Add(new UserVaultEntity
            {
                UserId = user, Salt = new byte[16], Iterations = 600_000, WrappedKey = new byte[61],
                PublicKey = [0x04, .. Enumerable.Repeat((byte)user[0], 64)], EncryptedPrivateKey = new byte[61],
                Settings = [1], SettingsVersion = 1
            });
        Db.SaveChanges();
    }

    public void Dispose() => m_Test.Dispose();

    private async Task BefriendAsync(string a, string b)
    {
        await SocialEndpoints.RequestFriendAsync(new FriendRequestDto(b), Db, TestDatabase.As(a), s_Time);
        var request = Db.Friendships.Single(f => f.RequesterId == a && f.AddresseeId == b);
        await SocialEndpoints.AcceptFriendAsync(request.Id, Db, TestDatabase.As(b), s_Time);
    }

    private Task<IResult> ShareAsync(string from, string to) =>
        SocialEndpoints.PutSharesAsync(new ShareUploadDto([new ShareItemDto(to, s_Week, [7])]), Db, TestDatabase.As(from), s_Time);

    [Fact]
    public async Task FriendRequest_ByNameOrEmail_IsPendingUntilAccepted()
    {
        (await SocialEndpoints.RequestFriendAsync(new FriendRequestDto("BOB@example.com"), Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<Ok>();
        (await SocialEndpoints.RequestFriendAsync(new FriendRequestDto("nobody"), Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<NotFound<string>>();
        (await SocialEndpoints.RequestFriendAsync(new FriendRequestDto("alice"), Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<BadRequest<string>>();

        var bobs = await SocialEndpoints.FriendsOfAsync(Db, "bob");
        bobs.Single().ShouldSatisfyAllConditions(f => f.Incoming.ShouldBeTrue(), f => f.Accepted.ShouldBeFalse(),
            f => f.UserName.ShouldBe("alice"));
        (await SocialEndpoints.PartnersOfAsync(Db, "alice")).ShouldBeEmpty();

        // Bob asking back accepts.
        (await SocialEndpoints.RequestFriendAsync(new FriendRequestDto("alice"), Db, TestDatabase.As("bob"), s_Time))
            .ShouldBeOfType<Ok>();
        (await SocialEndpoints.PartnersOfAsync(Db, "alice")).ShouldBe(["bob"]);
    }

    [Fact]
    public async Task Shares_OnlyGoToFriends_AndStopWhenTheFriendshipEnds()
    {
        (await ShareAsync("alice", "bob")).ShouldBeOfType<BadRequest<string>>();

        await BefriendAsync("alice", "bob");
        (await ShareAsync("alice", "bob")).ShouldBeOfType<NoContent>();
        (await ShareAsync("alice", "carol")).ShouldBeOfType<BadRequest<string>>();

        var received = (await SocialEndpoints.GetReceivedAsync(s_Week.AddDays(-7), Db, TestDatabase.As("bob"), s_Time))
            .ShouldBeOfType<Ok<List<ReceivedShareDto>>>().Value!;
        received.Single().ShouldSatisfyAllConditions(r => r.OwnerUserName.ShouldBe("alice"), r => r.Data.ShouldBe(new byte[] { 7 }),
            r => r.OwnerPublicKey[1].ShouldBe((byte)'a'));

        var friendship = Db.Friendships.Single();
        await SocialEndpoints.RemoveFriendAsync(friendship.Id, Db, TestDatabase.As("bob"));
        Db.ChangeTracker.Clear();
        Db.SharedStats.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Groups_ShareBetweenAcceptedMembers()
    {
        var created = (await SocialEndpoints.CreateGroupAsync(new CreateGroupDto("Family"), Db, TestDatabase.As("alice"), s_Time))
            .ShouldBeOfType<Ok<GroupDto>>().Value!;
        (await SocialEndpoints.InviteAsync(created.Id, new GroupInviteDto("carol"), Db, TestDatabase.As("bob"), s_Time))
            .ShouldBeOfType<NotFound<string>>(); // only the admin invites
        await SocialEndpoints.InviteAsync(created.Id, new GroupInviteDto("carol"), Db, TestDatabase.As("alice"), s_Time);

        (await SocialEndpoints.PartnersOfAsync(Db, "alice")).ShouldBeEmpty();
        (await SocialEndpoints.GroupsOfAsync(Db, "carol")).Single().Accepted.ShouldBeFalse();

        await SocialEndpoints.AcceptInvitationAsync(created.Id, Db, TestDatabase.As("carol"));
        (await SocialEndpoints.PartnersOfAsync(Db, "carol")).ShouldBe(["alice"]);
        (await ShareAsync("carol", "alice")).ShouldBeOfType<NoContent>();

        await SocialEndpoints.RemoveMemberAsync(created.Id, "carol", Db, TestDatabase.As("carol"));
        Db.ChangeTracker.Clear();
        Db.SharedStats.Count().ShouldBe(0);
        (await SocialEndpoints.GroupsOfAsync(Db, "carol")).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletingAnAccount_RemovesItsFriendshipsSharesAndGroups()
    {
        await BefriendAsync("alice", "bob");
        await ShareAsync("bob", "alice");
        await SocialEndpoints.CreateGroupAsync(new CreateGroupDto("Mine"), Db, TestDatabase.As("bob"), s_Time);

        var store = new Mock<IUserStore<ApplicationUser>>();
        var users = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        (await AccountDeletion.DeleteAsync(Db, users.Object, Db.Users.Single(u => u.Id == "bob"))).Succeeded.ShouldBeTrue();
        Db.ChangeTracker.Clear();
        Db.Friendships.Count().ShouldBe(0);
        Db.SharedStats.Count().ShouldBe(0);
        Db.Groups.Count().ShouldBe(0);
    }
}
