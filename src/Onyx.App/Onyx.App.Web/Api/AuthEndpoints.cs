using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/account/login", LoginHandler);
        routes.MapPost("/account/logout", WebLogOutHandler);
        
        var api = routes.MapGroup("/api");
        
        api.MapPost("/auth/login", LoginHandler);
        api.MapPost("/auth/refresh", RefreshHandler);
        api.MapPost("/auth/register", RegisterHandler);
        api.MapPost("/auth/refresh-token", RefreshTokenHandler);
        
        var authorized = api.MapGroup("/user").RequireAuthorization();
        authorized.MapGet("/profile", GetProfileHandler);
        authorized.MapPut("/profile", UpdateProfileHandler);
        authorized.MapPut("/password", ChangePasswordHandler);
        authorized.MapDelete("/account", DeleteAccountHandler);
        
        authorized.MapPost("/email/change", RequestEmailChangeHandler);
        authorized.MapPost("/email/confirm", ConfirmEmailHandler);
        
        authorized.MapPost("/2fa/enable", Enable2FaHandler);
        authorized.MapPost("/2fa/disable", Disable2FaHandler);
        authorized.MapGet("/2fa/recovery-codes", GetRecoveryCodesHandler);
        authorized.MapPost("/2fa/recovery-codes/regenerate", RegenerateRecoveryCodesHandler);
    }

    public static async Task<IResult> RefreshHandler(
        [FromBody] RefreshTokenRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] KeyAccessor keyAccessor)
    {
        var (user, _) = await JwtTools.ValidateAndGetUserFromToken(request.Token, userManager);

        if (user == null)
            return Results.Unauthorized();

        var newToken = JwtTools.GenerateToken(keyAccessor.ApplicationKey,
            user.Id, user.Email ?? user.Id, TimeSpan.FromDays(2));

        return Results.Ok(new { token = newToken });
    }

    public static async Task<IResult> LoginHandler(
        [FromBody] LoginRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] KeyAccessor keyAccessor)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null) return Results.Unauthorized();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return Results.Unauthorized();

        var token = JwtTools.GenerateToken(keyAccessor.ApplicationKey,
            user.Id, user.Email ?? user.Id, TimeSpan.FromDays(2));
        return Results.Ok(new { token });
    }

    public static async Task<IResult> RefreshTokenHandler(
        [FromBody] LoginRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] KeyAccessor keyAccessor)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null) return Results.Unauthorized();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return Results.Unauthorized();

        var token = JwtTools.GenerateRefreshToken(keyAccessor.ApplicationKey,
            user.Id, user.Email ?? user.Id);
        
        return Results.Ok(token);
    }
    
    public static async Task<IResult> RegisterHandler(
        [FromBody] RegisterRequest request,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await userManager.AddToRoleAsync(user, "User");
        return Results.Ok();
    }

    public static async Task<IResult> GetProfileHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        return Results.Ok(new
        {
            user.Email,
            user.UserName,
            user.PhoneNumber,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            user.TwoFactorEnabled
        });
    }

    public static async Task<IResult> UpdateProfileHandler(
        [FromBody] UpdateProfileRequest request,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        user.PhoneNumber = request.PhoneNumber;
        user.UserName = request.UserName;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    public static async Task<IResult> ChangePasswordHandler(
        [FromBody] ChangePasswordRequest request,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    public static async Task<IResult> DeleteAccountHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }
    
    public static async Task<IResult> RequestEmailChangeHandler(
        [FromBody] ChangeEmailRequest request,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var _ = await userManager.GenerateChangeEmailTokenAsync(user, request.NewEmail);

        // TODO: Send email with confirmation token
        
        return Results.Ok();
    }

    public static async Task<IResult> ConfirmEmailHandler(
        [FromQuery] string userId,
        [FromQuery] string token,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Results.NotFound();

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    public static async Task<IResult> Enable2FaHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var _ = await userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
        // TODO Send token via SMS/email
        await userManager.SetTwoFactorEnabledAsync(user, true);
        return Results.Ok();
    }

    public static async Task<IResult> Disable2FaHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    public static async Task<IResult> GetRecoveryCodesHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Results.Ok(recoveryCodes);
    }

    public static async Task<IResult> RegenerateRecoveryCodesHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Results.Ok();
    }

    public static Task WebLogOutHandler(HttpContext context)
    {
        context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Task.CompletedTask;
    }

    public static async Task<IResult> WebLoginHandler(
        [FromForm] WebLoginModel webLoginModel,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory, 
        HttpContext context)
    {
        var user = await signInManager.UserManager.FindByEmailAsync(webLoginModel.Email);

        if (user == null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(user, webLoginModel.Password, true, false);

        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }

        var principal = await claimsFactory.CreateAsync(user);
        await context.SignInAsync(IdentityConstants.ApplicationScheme, principal);

        return Results.Redirect(webLoginModel.Redirect);
    }

    public class WebLoginModel
    {
        public required string Redirect { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
    }
}