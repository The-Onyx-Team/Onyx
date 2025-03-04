using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.JSInterop;
using Onyx.App.Shared.Services.Auth;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

public class UserManager(
    UserManager<ApplicationUser> userManager, 
    IJSRuntime jsRuntime,
    IAntiforgery antiforgery,
    IHttpContextAccessor httpContextAccessor) : IUserManager
{
    public async Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri)
    {
        var user = new ApplicationUser
        {
            UserName = name,
            Email = email,
        };
        
        var result = await userManager.CreateAsync(user, password);
        
        if (!result.Succeeded)
            return new RegisterResult()
            {
                Success = false,
                Message = string.Join(", ", result.Errors.Select(x => x.Description))
            };
        
        var token = antiforgery.GetAndStoreTokens(httpContextAccessor.HttpContext!).RequestToken;
        
        var data = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token!},
            { "redirect", redirectUri },
            { "email", email },
            { "password", password }
        };

        await jsRuntime.InvokeVoidAsync("postRedirect", "/account/login", data);
        
        return new RegisterResult()
        {
            Success = true
        };
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string redirectUri)
    {
        var token = antiforgery.GetAndStoreTokens(httpContextAccessor.HttpContext!).RequestToken;
        
        var data = new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token!},
            { "redirect", redirectUri },
            { "email", email },
            { "password", password }
        };

        await jsRuntime.InvokeVoidAsync("postRedirect", "/account/login", data);
        
        return new LoginResult()
        {
            Success = true
        };
    }

}