using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

/// <summary>Deletes an account and everything that belongs to it.</summary>
public static class AccountDeletion
{
    /// <remarks>
    /// Devices, their (encrypted) usage, apps, the vault and group memberships are removed by cascading foreign keys. Groups the user administers
    /// are deleted here first: SQL Server does not allow a second cascade path from users to group memberships.
    /// </remarks>
    public static async Task<IdentityResult> DeleteAsync(ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        // Rows that point at two users don't cascade (SQL Server allows one cascade path per table).
        await dbContext.Friendships.Where(f => f.RequesterId == user.Id || f.AddresseeId == user.Id).ExecuteDeleteAsync();
        await dbContext.SharedStats.Where(s => s.OwnerId == user.Id || s.RecipientId == user.Id).ExecuteDeleteAsync();
        var groups = dbContext.Groups.Where(g => g.AdminId == user.Id).Select(g => g.Id);
        await dbContext.GroupHasUsers.Where(m => groups.Contains(m.GroupId)).ExecuteDeleteAsync();
        await dbContext.Groups.Where(g => g.AdminId == user.Id).ExecuteDeleteAsync();
        return await userManager.DeleteAsync(user);
    }
}
