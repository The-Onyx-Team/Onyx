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

        var authorized = api.MapGroup("/user").RequireAuthorization();
        authorized.MapGet("/profile", GetProfileHandler);
        authorized.MapPut("/profile", UpdateProfileHandler);
        authorized.MapPut("/password", ChangePasswordHandler);
        authorized.MapDelete("/account", DeleteAccountHandler);

        authorized.MapPost("/email/change", RequestEmailChangeHandler);
        authorized.MapPost("/email/confirm", ConfirmEmailHandler);

        authorized.MapPost("/2fa/enable", Enable2FaHandler);
        authorized.MapPost("/2fa/disable", Disable2FaHandler);
        authorized.MapPost("/2fa/recovery-codes", RegenerateRecoveryCodesHandler);
    }

    /// <summary>
    /// Accepts and validates a refresh token, returning a new JWT token.
    /// </summary>
    public static async Task<IResult> RefreshHandler(
        [FromBody] RefreshTokenRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] KeyAccessor keyAccessor)
    {
        try
        {
            var (user, _) = await JwtTools.ValidateAndGetUserFromToken(request.Token, userManager);

            if (user == null)
                return Results.Unauthorized();

            var newToken = JwtTools.GenerateToken(keyAccessor.ApplicationKey,
                user.Id, user.Email ?? user.Id, TimeSpan.FromDays(2));

            return Results.Ok(new { token = newToken });
        }
        catch (Exception e)
        {
            return Results.BadRequest(e.GetType().Name);
        }
    }

    /// <summary>
    /// Accepts and validates a login request, returning a JWT token and refresh token.
    /// </summary>
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

        var refreshToken = JwtTools.GenerateRefreshToken(keyAccessor.ApplicationKey,
            user.Id, user.Email ?? user.Id);

        return Results.Ok(new { token, refreshToken });
    }

    /// <summary>
    /// Accepts and validates a registration request, creating a new user.
    /// </summary>
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
        return !result.Succeeded ? Results.BadRequest(result.Errors) : Results.Ok();
    }

    /// <summary>
    /// Accepts a request to get the current user's profile.
    /// </summary>
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

    /// <summary>
    /// Accepts a request to update the current user's profile.
    /// </summary>
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

    /// <summary>
    /// Accepts a request to change the current user's password.
    /// </summary>
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

    /// <summary>
    /// Accepts a request to delete the current user's account.
    /// </summary>
    public static async Task<IResult> DeleteAccountHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>
    /// Accepts a request to change the current user's email.
    /// </summary>
    ///  TODO TEST
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

    /// <summary>
    /// Accepts a request to confirm the current user's email change.
    /// </summary>
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

    /// <summary>
    /// Accepts a request to enable two-factor authentication for the current user.
    /// </summary>
    /// TODO TEST
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

    /// <summary>
    /// Accepts a request to disable two-factor authentication for the current user.
    /// </summary>
    public static async Task<IResult> Disable2FaHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>
    /// Generates and returns a list of recovery codes for the current user.
    /// </summary>
    public static async Task<IResult> RegenerateRecoveryCodesHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Results.Ok(codes);
    }

    /// <summary>
    /// Logs out the current user and redirects to the home page.
    /// </summary>
    public static IResult WebLogOutHandler(HttpContext context)
    {
        context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.Redirect("/");
    }

    /// <summary>
    /// Attempts to log in a user using a form submission. Automatic redirect on success.
    /// </summary>
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