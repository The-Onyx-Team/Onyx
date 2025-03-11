using Microsoft.AspNetCore.Identity;
using Onyx.App.Shared.Services.Auth;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

public class UserProvider(IHttpContextAccessor contextAccessor, UserManager<ApplicationUser> userManager) : IUserProvider
{
    private User? m_User;
    private ApplicationUser? m_IdentityUser;

    private async Task EnsureUserAsync()
    {
        if (m_User is null || m_IdentityUser is null)
            await GetRequiredUserAsync();
    }
    
    /// <inheritdoc />
    public async Task<User> GetRequiredUserAsync()
    {
        var context = contextAccessor.HttpContext;
        if (context == null)
        {
            throw new InvalidOperationException("HttpContext is null");
        }
        
        var user = await userManager.GetUserAsync(context.User);

        m_IdentityUser = user ?? throw new InvalidOperationException("User not found");
        
        m_User = new User
        {
            Name = user.UserName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Has2Fa = user.TwoFactorEnabled
        };
        
        return m_User;
    }

    public async Task<string> GetAuthenticatorKeyAsync()
    {
        await EnsureUserAsync();
        
        var key = await userManager.GetAuthenticatorKeyAsync(m_IdentityUser!);

        if (!string.IsNullOrEmpty(key)) return key;
        
        await userManager.ResetAuthenticatorKeyAsync(m_IdentityUser!);
        key = await userManager.GetAuthenticatorKeyAsync(m_IdentityUser!);

        return key!;
    }
}