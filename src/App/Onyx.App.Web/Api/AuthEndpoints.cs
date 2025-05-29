using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Onyx.App.Shared.Pages.Account;
using Onyx.App.Shared.Pages.Account.Manage;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var accountGroup = routes.MapGroup("/account");
        accountGroup.MapPost("/perform-external-login", ExternalLoginHandler);
        accountGroup.MapPost("/login", WebLoginHandler);
        accountGroup.MapPost("/logout", WebLogOutHandler);

        var manageGroup = accountGroup.MapGroup("/manage").RequireAuthorization();
        manageGroup.MapPost("/link-external-login", LinkExternalLoginHandler);
        manageGroup.MapPost("/download-personal-data", DownloadPersonalDataHandler);

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
        authorized.MapGet("/email/confirm", ConfirmEmailHandler);

        authorized.MapPost("/2fa/enable", Enable2FaHandler);
        authorized.MapPost("/2fa/disable", Disable2FaHandler);
        authorized.MapPost("/2fa/recovery-codes", RegenerateRecoveryCodesHandler);
    }

    private static async Task<IResult> DownloadPersonalDataHandler(HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] AuthenticationStateProvider authenticationStateProvider)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
        }

        var userId = await userManager.GetUserIdAsync(user);

        var personalDataProps = typeof(ApplicationUser).GetProperties()
            .Where(prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
        var personalData = personalDataProps.ToDictionary(p => p.Name, p => p.GetValue(user)?.ToString() ?? "null");

        var logins = await userManager.GetLoginsAsync(user);
        foreach (var l in logins)
        {
            personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
        }

        personalData.Add("Authenticator Key", (await userManager.GetAuthenticatorKeyAsync(user))!);
        var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

        context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
        return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "PersonalData.json");
    }

    private static async Task<ChallengeHttpResult> LinkExternalLoginHandler(HttpContext context,
        [FromServices] SignInManager<ApplicationUser> signInManager, [FromForm] string provider)
    {
        // Clear the existing external cookie to ensure a clean login process
        await context.SignOutAsync(IdentityConstants.ExternalScheme);

        var redirectUrl = UriHelper.BuildRelative(context.Request.PathBase, "/account/manage/external",
            QueryString.Create("Action", ManageExternalLogin.LinkLoginCallbackAction));

        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl,
            signInManager.UserManager.GetUserId(context.User));
        return TypedResults.Challenge(properties, [provider]);
    }

    private static ChallengeHttpResult ExternalLoginHandler(HttpContext context,
        [FromServices] SignInManager<ApplicationUser> signInManager, [FromForm] string provider,
        [FromForm] string returnUrl)
    {
        IEnumerable<KeyValuePair<string, StringValues>> query =
            [new("ReturnUrl", returnUrl), new("Action", ExternalLogin.LoginCallbackAction)];

        var redirectUrl = UriHelper.BuildRelative(context.Request.PathBase, "/Account/ExternalLogin",
            QueryString.Create(query));

        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return TypedResults.Challenge(properties, [provider]);
    }

    /// <summary>
    /// Accepts and validates a refresh token, returning a new JWT token.
    /// </summary>
    public static async Task<IResult> RefreshHandler(
        [FromBody] RefreshTokenDto dto,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] KeyAccessor keyAccessor)
    {
        try
        {
            var (user, _) = await JwtTools.ValidateAndGetUserFromToken(dto.Token, userManager);

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
        [FromBody] LoginDto dto,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] KeyAccessor keyAccessor)
    {
        var user = await userManager.FindByEmailAsync(dto.Email);
        if (user == null) return Results.BadRequest("Invalid email or password");

        if (!await userManager.CheckPasswordAsync(user, dto.Password))
            return Results.BadRequest("Invalid email or password");

        var token = JwtTools.GenerateToken(keyAccessor.ApplicationKey,
            user.Id, user.Email ?? user.Id, TimeSpan.FromDays(2));

        var refreshToken = JwtTools.GenerateRefreshToken(keyAccessor.ApplicationKey,
            user.Id, user.Email ?? user.Id);

        return Results.Ok(new LoginResultDto(token, refreshToken));
    }

    /// <summary>
    /// Accepts and validates a registration request, creating a new user.
    /// </summary>
    public static async Task<IResult> RegisterHandler(
        [FromBody] RegisterDto dto,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = new ApplicationUser
        {
            UserName = dto.Name,
            Email = dto.Email,
            Groups = new List<Groups>()
        };
        
        user.Groups = new List<Groups>();

        var result = await userManager.CreateAsync(user, dto.Password);
        return !result.Succeeded ? Results.BadRequest(result.Errors) : Results.Ok(new object());
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
        [FromBody] UpdateProfileDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        user.PhoneNumber = dto.PhoneNumber;
        user.UserName = dto.UserName;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>
    /// Accepts a request to change the current user's password.
    /// </summary>
    public static async Task<IResult> ChangePasswordHandler(
        [FromBody] ChangePasswordDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
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
        [FromBody] ChangeEmailDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var _ = await userManager.GenerateChangeEmailTokenAsync(user, dto.NewEmail);

        // TODO: Send email with confirmation token

        return Results.Ok();
    }

    /// <summary>
    /// Accepts a request to confirm the current user's email change.
    /// </summary>
    public static async Task<IResult> ConfirmEmailHandler(
        [FromQuery] string userId,
        [FromQuery] string code,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return Results.NotFound();

        Console.WriteLine(code);
        var result = await userManager.ConfirmEmailAsync(user, code);
        return result.Succeeded ? Results.Redirect("/account/confirmEmail") : Results.BadRequest(result.Errors);
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
    public static async Task<IResult> WebLogOutHandler(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
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
            return Results.Redirect("/account/login?error=Invalid login attempt");
        }

        var result = await signInManager.PasswordSignInAsync(user, webLoginModel.Password, true, false);

        if (!result.Succeeded)
        {
            return Results.Redirect("/account/login?error=Invalid login attempt");
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