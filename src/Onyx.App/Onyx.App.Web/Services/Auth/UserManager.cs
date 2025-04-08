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
using AuthenticationScheme = Onyx.App.Shared.Services.Auth.AuthenticationScheme;

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

    public Task LogoutAsync()
    {
        try
        {
            signInManager.SignOutAsync();
        }
        catch (Exception)
        {
            // ignored
        }

        return Task.CompletedTask;
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

    public async Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user)
    {
        var appUser = await userManager.FindByEmailAsync(user.Email!);
        
        if (appUser is null) return [];
        
        return (await userManager.GetLoginsAsync(appUser)).Select(l =>
            new ExternalLoginData()
            {
                ProviderDisplayName = l.ProviderDisplayName!,
                ProviderKey = l.ProviderKey,
                LoginProvider = l.LoginProvider,
                Principal = null!
            });
    }

    public async Task<bool> RemoveLogin(User user, string loginProvider, string providerKey)
    {
        var dbUser = await userManager.FindByIdAsync(user.Id!);
        var result = await userManager.RemoveLoginAsync(dbUser!,loginProvider,providerKey);
        return result.Succeeded;
    }

    public async Task<bool> ChangeEmail(User user, string newEmail)
    {
        var existingUser = await userManager.FindByEmailAsync(newEmail);
        if (existingUser != null)
        {
            return false;
        }
        var dbUser = await userManager.FindByIdAsync(user.Id!);
        newEmail = newEmail.ToUpper();
        dbUser!.Email = newEmail;
        try
        {
            await userManager.UpdateNormalizedEmailAsync(dbUser);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
        return true;   
    }

    public async Task<bool> ChangePhoneNumber(User user, string phoneNumber)
    {
        var dbUser = await userManager.FindByIdAsync(user.Id!);
        dbUser!.PhoneNumber = phoneNumber;
        var result = await userManager.UpdateAsync(dbUser);
            
        return result.Succeeded;
    }

    public async Task<bool> ChangePassword(User user, string oldPassword, string newPassword)
    {
        var dbUser = await userManager.FindByIdAsync(user.Id!);
        try
        {
            if (oldPassword != newPassword)
            {
                if (dbUser!.PasswordHash == oldPassword.GetHashCode().ToString())
                {
                    dbUser.PasswordHash = newPassword.GetHashCode().ToString();
                }
                else
                {
                    Console.WriteLine("Old password does not match!");
                    return false;
                }
            }
            else
            {
                Console.WriteLine("New Password can't be old Password!");
                return false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }

        var result = await userManager.UpdateAsync(dbUser);
        return result.Succeeded;
    }

    public async Task<bool> SendChangePasswordEmail(string id, string email)
    {
        // Find the user by ID
        var dbUser = await userManager.FindByIdAsync(id);
        if (dbUser == null || dbUser.Email != email)
        {
            Console.WriteLine("User not found or email mismatch.");
            return false;
        }

        try
        {
            // Generate a password reset token
            var token = await userManager.GeneratePasswordResetTokenAsync(dbUser);

            // Encode the token for safe transmission in a URL
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            // Generate the callback URL for resetting the password
            var callbackUrl = navigationManager.GetUriWithQueryParameters(
                navigationManager.ToAbsoluteUri("Account/ResetPassword").AbsoluteUri,
                new Dictionary<string, object?>
                {
                    ["userId"] = dbUser.Id,
                    ["token"] = encodedToken
                });

            // Send the password reset email using the email sender service
            await emailSender.SendPasswordResetLinkAsync(dbUser, email, HtmlEncoder.Default.Encode(callbackUrl));

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while sending the password reset email: {ex.Message}");
            return false;
        }
    }


    public async Task<bool> ChangeUserName(User user, string userName)
    {
        var existingUser = await userManager.FindByNameAsync(userName);
        if (existingUser != null)
        {
            return false;
        }
        var dbUser = await userManager.FindByIdAsync(user.Id!);
        dbUser!.UserName = userName;
        var result = await userManager.UpdateAsync(dbUser);
        return result.Succeeded;
    }

    public async Task<string[]> GetRecoveryCodes(User user)
    {
        var dbUser = await userManager.FindByIdAsync(user.Id!);
        try
        {
            if (dbUser!.TwoFactorEnabled)
            {
                return userManager.GenerateNewTwoFactorRecoveryCodesAsync(dbUser, 10).Result!.ToArray();
            }

            Console.WriteLine("2Fa not enabled");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null!;
        }
        return null!;
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