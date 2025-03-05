using Microsoft.AspNetCore.Identity;
using Onyx.App.Shared.Services.Auth;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

public class UserProvider(IHttpContextAccessor contextAccessor, UserManager<ApplicationUser> userManager) : IUserProvider
{
    /// <inheritdoc />
    public async Task<User> GetRequiredUserAsync()
    {
        var context = contextAccessor.HttpContext;
        if (context == null)
        {
            throw new InvalidOperationException("HttpContext is null");
        }
        
        var user = await userManager.GetUserAsync(context.User);
        
        if (user == null)
        {
            throw new InvalidOperationException("User not found");
        }
        
        return new User
        {
            Name = user.UserName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Has2Fa = user.TwoFactorEnabled
        };
    }
}