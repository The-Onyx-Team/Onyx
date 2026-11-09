using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

/// <summary>Deletes an account and everything that belongs to it.</summary>
public static class AccountDeletion
{
    /// <remarks>
    /// Devices, their usage and group memberships are removed by cascading foreign keys. Groups the user administers
    /// are deleted here first: SQL Server does not allow a second cascade path from users to group memberships.
    /// </remarks>
    public static async Task<IdentityResult> DeleteAsync(ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        await dbContext.Groups.Where(g => g.AdminId == user.Id).ExecuteDeleteAsync();
        return await userManager.DeleteAsync(user);
    }
}
