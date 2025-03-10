using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using Onyx.App.Shared.Services.Auth;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

public class UserManager(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IUserStore<ApplicationUser> userStore,
    NavigationManager navigationManager,
    ILogger<UserManager> logger,
    IJSRuntime jsRuntime,
    IAntiforgery antiforgery,
    IEmailSender<ApplicationUser> emailSender,
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
            { "__RequestVerificationToken", token! },
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
            { "__RequestVerificationToken", token! },
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

    public async Task<ExternalLoginData> GetExternalLoginDataAsync()
    {
        var info = await signInManager.GetExternalLoginInfoAsync();

        if (info == null)
            throw new UnauthorizedAccessException();

        return new ExternalLoginData()
        {
            ProviderDisplayName = info.ProviderDisplayName!,
            ProviderKey = info.ProviderKey,
            LoginProvider = info.LoginProvider,
            Principal = info.Principal
        };
    }

    public async Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl,
        EmailInputModel input)
    {
        if (externalLoginInfo is null)
        {
            throw new Exception();
        }

        var result = await signInManager.ExternalLoginSignInAsync(
            externalLoginInfo.LoginProvider,
            externalLoginInfo.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (result.Succeeded)
        {
            logger.LogInformation(
                "{Name} logged in with {LoginProvider} provider",
                externalLoginInfo.Principal.Identity?.Name,
                externalLoginInfo.LoginProvider);
            navigationManager.NavigateTo(returnUrl ?? "/");
            return;
        }

        if (result.IsLockedOut)
        {
            navigationManager.NavigateTo("Account/Lockout");
            return;
        }

        if (externalLoginInfo.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
        {
            var email = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Email);

            input.Email = email ?? "example@here.com";
            
            if (email is not null)
                await HandleNewExternalLoginAsync(externalLoginInfo, input, returnUrl);
        }
    }

    public async Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input,
        string? returnUrl)
    {
        if (externalLoginInfo is null)
        {
            navigationManager.NavigateTo("account/login");
            return;
        }

        var emailStore = GetEmailStore();
        var user = CreateUser();

        await userManager.SetUserNameAsync(user, GetValidUsername(input.Email));
        await emailStore.SetEmailAsync(user, input.Email, CancellationToken.None);

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
            throw new Exception($"Error: {string.Join(",", result.Errors.Select(error => error.Description))}");
        result = await userManager.AddLoginAsync(user, new UserLoginInfo(
            externalLoginInfo.LoginProvider,
            externalLoginInfo.ProviderKey,
            externalLoginInfo.ProviderDisplayName));

        if (!result.Succeeded)
            throw new Exception($"Error: {string.Join(",", result.Errors.Select(error => error.Description))}");
        logger.LogInformation("User created an account using {Name} provider",
            externalLoginInfo.LoginProvider);

        var userId = await userManager.GetUserIdAsync(user);
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        var callbackUrl = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
            new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code });
        await emailSender.SendConfirmationLinkAsync(user, input.Email,
            HtmlEncoder.Default.Encode(callbackUrl));

        if (userManager.Options.SignIn.RequireConfirmedAccount)
        {
            var uri = navigationManager.GetUriWithQueryParameters("Account/RegisterConfirmation",
                new Dictionary<string, object?> { ["email"] = input.Email });
            navigationManager.NavigateTo(uri);
        }

        await signInManager.SignInAsync(user, isPersistent: false, externalLoginInfo.LoginProvider);
        navigationManager.NavigateTo(returnUrl ?? "/");

        throw new Exception($"Error: {string.Join(",", result.Errors.Select(error => error.Description))}");
    }

    public async Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas()
    {
        return (await signInManager.GetExternalAuthenticationSchemesAsync()).Select(l =>
            new AuthenticationScheme(l.Name, l.DisplayName!));
    }

    private ApplicationUser CreateUser()
    {
        try
        {
            return Activator.CreateInstance<ApplicationUser>();
        }
        catch
        {
            throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                                                $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor");
        }
    }
    
    public static string GetValidUsername(string email)
    {
        var username = email.Split('@')[0];
        var validUsername = new string(username.Where(char.IsLetterOrDigit).ToArray());
        return validUsername;
    }

    private IUserEmailStore<ApplicationUser> GetEmailStore() => (IUserEmailStore<ApplicationUser>)userStore;
}