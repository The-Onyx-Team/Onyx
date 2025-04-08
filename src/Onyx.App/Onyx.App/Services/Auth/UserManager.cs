using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Onyx.App.Services.Api;
using Onyx.App.Shared.Services.Auth;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Services.Auth;

public class UserManager(AuthenticationStateProvider authenticationStateProvider, AuthApi api,UserManager<ApplicationUser> userManager) : IUserManager
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    
    public async Task<RegisterResult> RegisterAsync(string name, string email, string password, string redirectUri)
    {
        var result = await api.RegisterAsync(name, email, password);

        if (result is null)
            return new RegisterResult
            {
                Success = false,
                Message = "Api error",
            };

        if (result.Success)
            return new RegisterResult
            {
                Success = true,
            };

        return new RegisterResult
        {
            Success = false,
            Message = result.Message,
        };
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string redirectUri)
    {
        var result = await api.LoginAsync(email, password);

        if (result is null)
        {
            await (authenticationStateProvider as MauiAuthenticationStateProvider)!
                .UpdateAuthenticationStateAsync(null);
            return new LoginResult()
            {
                Success = false,
                Message = "Invalid email or password"
            };
        }

        try
        {
            var keyHandler = new JwtSecurityTokenHandler();
            var token = keyHandler.ReadJwtToken(result.Token);
            var userId = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
            var userName = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
            var role = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Role)?.Value;
            var phone = token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.MobilePhone)?.Value;
            var amr = token.Claims.FirstOrDefault(x => x.Type == "amr")?.Value;

            await (authenticationStateProvider as MauiAuthenticationStateProvider)!
                .UpdateAuthenticationStateAsync(new UserSession()
                {
                    Token = result.Token,
                    RefreshToken = result.RefreshToken,
                    Email = email,
                    Name = userName,
                    Id = userId,
                    Role = role,
                    PhoneNumber = phone,
                    Has2Fa = amr == "mfa"
                });

            return new LoginResult
            {
                Success = true
            };
        }
        catch (Exception)
        {
            return new LoginResult()
            {
                Success = false,
                Message = "Failed to parse JWT Token"
            };
        }
    }

    public Task LogoutAsync()
    {
        return (authenticationStateProvider as MauiAuthenticationStateProvider)!.UpdateAuthenticationStateAsync(null);
    }

    public Task<ExternalLoginData> GetExternalLoginDataAsync()
    {
        throw new NotImplementedException();
    }

    public Task HandleExternalLoginAsync(ExternalLoginData externalLoginInfo, string? returnUrl, EmailInputModel input)
    {
        throw new NotImplementedException();
    }

    public Task HandleNewExternalLoginAsync(ExternalLoginData? externalLoginInfo, EmailInputModel input,
        string? returnUrl)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemas()
    {
        return Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
    }

    public Task<IEnumerable<ExternalLoginData>?> GetLoginsAsync(User user)
    {
        return Task.FromResult<IEnumerable<ExternalLoginData>>([])!;
    }

    public async Task<bool> RemoveLogin(User user, string loginProvider, string providerKey)
    {
        var dbUser = await _userManager.FindByIdAsync(user.Id!);
        var result = await _userManager.RemoveLoginAsync(dbUser!,loginProvider,providerKey);
        return result.Succeeded;
    }

    public async Task<bool> ChangeEmail(User user, string newEmail)
    {
        var existingUser = await _userManager.FindByEmailAsync(newEmail);
        if (existingUser != null)
        {
            return false;
        }
        var dbUser = await _userManager.FindByIdAsync(user.Id!);
        newEmail = newEmail.ToUpper();
        dbUser!.Email = newEmail;
        try
        {
            await _userManager.UpdateNormalizedEmailAsync(dbUser);
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
         
        var dbUser = await _userManager.FindByIdAsync(user.Id!);
            dbUser!.PhoneNumber = phoneNumber;
            var result = await _userManager.UpdateAsync(dbUser);
            
        return result.Succeeded;
    }

    public async Task<bool> ChangePassword(User user, string oldPassword, string newPassword)
    {
        var dbUser = await _userManager.FindByIdAsync(user.Id!);
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
        var result = await _userManager.UpdateAsync(dbUser);
        return result.Succeeded;
    }

    public Task<bool> SendChangePasswordEmail(string email)
    {
        
        throw new NotImplementedException();
    }

    public async Task<bool> ChangeUserName(User user, string userName)
    {
        var existingUser = await _userManager.FindByNameAsync(userName);
        if (existingUser != null)
        {
            return false;
        }
        var dbUser = await _userManager.FindByIdAsync(user.Id!);
        dbUser!.UserName = userName;
        var result = await _userManager.UpdateAsync(dbUser);
        return result.Succeeded;
    }
    

    public async Task<string[]> GetRecoveryCodes(User user)
    {
        var dbUser = await _userManager.FindByIdAsync(user.Id!);
        try
        {
            if (dbUser!.TwoFactorEnabled)
            {
                return _userManager.GenerateNewTwoFactorRecoveryCodesAsync(dbUser, 10).Result!.ToArray();
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
}