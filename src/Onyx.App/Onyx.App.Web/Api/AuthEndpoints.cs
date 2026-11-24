using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Web.Services;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        // Browser redirects for external logins (Google, Microsoft, GitHub). Everything else is API.
        var accountGroup = routes.MapGroup("/account");
        // Plain form posts from the WebAssembly app, which has no antiforgery token; they only start a redirect.
        accountGroup.MapPost("/perform-external-login", ExternalLogins.StartAsync).DisableAntiforgery();
        accountGroup.MapGet("/external-callback", ExternalLogins.CallbackAsync);
        accountGroup.MapGet("/app-external-login", ExternalLogins.StartAppAsync).RequireRateLimiting(Program.AuthRateLimit);
        accountGroup.MapPost("/link-external-login", ExternalLogins.StartLinkAsync).DisableAntiforgery();
        accountGroup.MapGet("/link-external-callback", ExternalLogins.LinkCallbackAsync);
        accountGroup.MapPost("/logout", WebLogOutHandler).DisableAntiforgery();

        var api = routes.MapGroup("/api");

        // Unauthenticated endpoints are rate limited per client address against password guessing and email spam.
        var auth = api.MapGroup("/auth").RequireRateLimiting(Program.AuthRateLimit);
        auth.MapPost("/login", LoginHandler);
        auth.MapPost("/refresh", RefreshHandler);
        auth.MapPost("/register", RegisterHandler);
        auth.MapPost("/forgot-password", ForgotPasswordHandler);
        auth.MapPost("/reset-password", ResetPasswordHandler);
        auth.MapPost("/confirm-email", ConfirmEmailHandler);
        auth.MapGet("/schemes", ExternalLogins.SchemesAsync);
        auth.MapPost("/external/exchange", ExternalLogins.ExchangeAsync);
        auth.MapGet("/external/pending", ExternalLogins.PendingAsync);
        auth.MapPost("/external/register", ExternalLogins.RegisterAsync);

        var authorized = api.MapGroup("/user").RequireAuthorization(Program.ApiPolicy);
        authorized.MapGet("/profile", GetProfileHandler);
        authorized.MapPut("/profile", UpdateProfileHandler);
        authorized.MapPut("/avatar", SetAvatarHandler);
        authorized.MapDelete("/avatar", DeleteAvatarHandler);
        authorized.MapPut("/password", ChangePasswordHandler);
        authorized.MapDelete("/account", DeleteAccountHandler);
        authorized.MapPost("/consent", ConsentHandler);
        authorized.MapGet("/personal-data", PersonalDataHandler).Produces<PersonalDataDto>();

        authorized.MapGet("/logins", ExternalLogins.ListAsync).Produces<List<ExternalLoginDto>>();
        authorized.MapDelete("/logins/{provider}", ExternalLogins.RemoveAsync);
        authorized.MapPost("/logins/link-ticket", ExternalLogins.LinkTicketAsync).Produces<LinkTicketDto>();

        authorized.MapPost("/email/change", RequestEmailChangeHandler);

        authorized.MapGet("/2fa", TwoFactorStatusHandler).Produces<TwoFactorStatusDto>();
        authorized.MapGet("/2fa/key", AuthenticatorKeyHandler).Produces<TwoFactorKeyDto>();
        authorized.MapPost("/2fa/enable", Enable2FaHandler).Produces<RecoveryCodesDto>();
        authorized.MapPost("/2fa/disable", Disable2FaHandler);
        authorized.MapPost("/2fa/recovery-codes", NewRecoveryCodesHandler).Produces<RecoveryCodesDto>();
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
            var (user, _) = await JwtTools.ValidateAndGetUserFromToken(dto.Token, userManager,
                keyAccessor.ApplicationKey);

            if (user == null)
                return Results.Unauthorized();

            // Rotate the refresh token too, so a device that keeps syncing never has to log in again.
            return Results.Ok(IssueTokens(user, keyAccessor));
        }
        catch (Exception e)
        {
            return Results.BadRequest(e.GetType().Name);
        }
    }

    internal static LoginResultDto IssueTokens(ApplicationUser user, KeyAccessor keyAccessor)
    {
        var userName = user.Email ?? user.Id;
        var token = JwtTools.GenerateToken(keyAccessor.ApplicationKey, user.Id, userName,
            JwtTools.AccessTokenLifetime);
        var refreshToken = JwtTools.GenerateRefreshToken(keyAccessor.ApplicationKey, user.Id, userName,
            user.SecurityStamp);
        return new LoginResultDto(token, refreshToken);
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

        if (await TwoFactorCheckAsync(userManager, user, dto.TwoFactorCode) is { } twoFactor)
            return twoFactor;

        return Results.Ok(IssueTokens(user, keyAccessor));
    }

    /// <summary>
    /// Accepts and validates a registration request, creating a new user.
    /// </summary>
    public static async Task<IResult> RegisterHandler(
        [FromBody] RegisterDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] IEmailSender<ApplicationUser> emailSender,
        [FromServices] IOptions<OnyxServerOptions> serverOptions,
        [FromServices] TimeProvider time)
    {
        if (!serverOptions.Value.AllowRegistration)
            return Results.Problem(OnyxServerOptions.RegistrationClosedMessage, statusCode: StatusCodes.Status403Forbidden);

        // GDPR: the account is only created with the user's explicit agreement, recorded with the policy version.
        if (!dto.AcceptPrivacyPolicy)
            return Results.Problem(ConsentRequiredMessage, statusCode: StatusCodes.Status400BadRequest);

        var now = time.GetUtcNow().UtcDateTime;
        var user = new ApplicationUser
        {
            UserName = dto.Name,
            Email = dto.Email,
            CreatedAt = now,
            PrivacyConsentAt = now,
            PrivacyPolicyVersion = Truncate(dto.PrivacyPolicyVersion, 20),
        };

        var result = await userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await SendConfirmationAsync(context, userManager, emailSender, user);
        return Results.Ok(new object());
    }

    public const string PlainProfileMessage =
        "Your phone number and profile picture are end-to-end encrypted now. Update the app to change them.";

    public const string ConsentRequiredMessage =
        "Please read the privacy policy and confirm that you agree to it. Older app versions can't do this; update the app.";

    internal static async Task SendConfirmationAsync(HttpContext context, UserManager<ApplicationUser> userManager,
        IEmailSender<ApplicationUser> emailSender, ApplicationUser user)
    {
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/account/confirmEmail",
            new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });
        await emailSender.SendConfirmationLinkAsync(user, user.Email!, System.Text.Encodings.Web.HtmlEncoder.Default.Encode(link));
    }

    internal static string? Truncate(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];

    /// <summary>Confirms an email address with the code from the confirmation email.</summary>
    public static async Task<IResult> ConfirmEmailHandler(
        [FromBody] ConfirmEmailDto dto,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(dto.UserId);
        if (user is null)
            return Results.BadRequest("The link is invalid.");
        var result = await userManager.ConfirmEmailAsync(user, dto.Code);
        return result.Succeeded ? Results.Ok() : Results.BadRequest("The link is invalid or has expired.");
    }

    /// <summary>Sets a new password with the code from the reset email.</summary>
    public static async Task<IResult> ResetPasswordHandler(
        [FromBody] ResetPasswordDto dto,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByIdAsync(dto.UserId);
        if (user is null)
            return Results.BadRequest("The link is invalid.");
        // ResetPasswordAsync checks the emailed code; without it anyone knowing the user id could set a password.
        var result = await userManager.ResetPasswordAsync(user, dto.Code, dto.Password);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>Records that the signed-in user agreed to (a new version of) the privacy policy.</summary>
    public static async Task<IResult> ConsentHandler(
        [FromBody] ConsentDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] TimeProvider time)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();
        user.PrivacyConsentAt = time.GetUtcNow().UtcDateTime;
        user.PrivacyPolicyVersion = Truncate(dto.PrivacyPolicyVersion, 20);
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>Everything the server stores about the user in plain text (GDPR Art. 15). The encrypted data is
    /// added by the client, which can decrypt it.</summary>
    public static async Task<IResult> PersonalDataHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] ApplicationDbContext db)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var devices = await db.Devices.AsNoTracking().Where(d => d.UserId == user.Id)
            .Select(d => new DeviceDto { Id = d.Id, Name = d.Name!, Platform = d.Platform, LastSeen = d.LastSeen })
            .ToListAsync();
        var vault = await db.UserVaults.AsNoTracking().Where(v => v.UserId == user.Id)
            .Select(v => (DateTime?)v.CreatedAt).FirstOrDefaultAsync();

        return Results.Ok(new PersonalDataDto(
            user.Id,
            user.UserName,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled,
            user.Avatar is { Length: > 0 },
            Utc(user.CreatedAt),
            Utc(user.PrivacyConsentAt),
            user.PrivacyPolicyVersion,
            (await userManager.GetLoginsAsync(user)).Select(l => l.ProviderDisplayName ?? l.LoginProvider).ToList(),
            devices.Select(d => { d.LastSeen = Utc(d.LastSeen); return d; }).ToList(),
            Utc(vault),
            await db.EncryptedUsages.CountAsync(u => u.Device!.UserId == user.Id),
            await db.EncryptedApps.CountAsync(a => a.UserId == user.Id),
            await SocialEndpoints.FriendsOfAsync(db, user.Id),
            await SocialEndpoints.GroupsOfAsync(db, user.Id),
            await db.SharedStats.CountAsync(s => s.OwnerId == user.Id),
            await db.SharedStats.CountAsync(s => s.RecipientId == user.Id)));
    }

    private static DateTime? Utc(DateTime? time) =>
        time is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;

    /// <summary>For an account with two-factor authentication: null if <paramref name="code"/> is valid, otherwise
    /// the answer (428 without a code, 400 with a wrong one).</summary>
    internal static async Task<IResult?> TwoFactorCheckAsync(UserManager<ApplicationUser> userManager,
        ApplicationUser user, string? code)
    {
        if (!user.TwoFactorEnabled)
            return null;
        if (string.IsNullOrWhiteSpace(code))
            return Results.Problem(TwoFactorAuth.RequiredMessage, statusCode: TwoFactor.RequiredStatus);
        return await TwoFactorAuth.VerifyAsync(userManager, user, code)
            ? null
            : Results.BadRequest(TwoFactorAuth.InvalidCodeMessage);
    }

    public static async Task<IResult> TwoFactorStatusHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();
        return Results.Ok(new TwoFactorStatusDto(user.TwoFactorEnabled,
            user.TwoFactorEnabled ? await userManager.CountRecoveryCodesAsync(user) : 0));
    }

    /// <summary>The authenticator app key of the signed-in user, created on first use.</summary>
    public static async Task<IResult> AuthenticatorKeyHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var key = await TwoFactorAuth.KeyAsync(userManager, user);
        return Results.Ok(new TwoFactorKeyDto(TwoFactorAuth.FormatKey(key),
            TwoFactorAuth.Uri(user.Email ?? user.UserName ?? user.Id, key)));
    }

    /// <summary>
    /// Sends a password reset link to the address if it has an account. Always answers 200, so the response does
    /// not reveal which addresses are registered.
    /// </summary>
    public static async Task<IResult> ForgotPasswordHandler(
        [FromBody] ForgotPasswordDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] IEmailSender<ApplicationUser> emailSender)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/";
        await PasswordReset.SendLinkAsync(userManager, emailSender, dto.Email, baseUrl);
        return Results.Ok(new object());
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

        return Results.Ok(ProfileService.ToDto(user, await userManager.HasPasswordAsync(user)));
    }

    /// <summary>Sets the current user's profile picture (PNG, JPEG or WebP, at most 512 KB).</summary>
    public static async Task<IResult> SetAvatarHandler(
        [FromBody] AvatarDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] ApplicationDbContext db)
    {
        if (Avatar.Validate(dto.Data, dto.ContentType) is { } error)
            return Results.BadRequest(error);

        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();
        // With encryption set up, the picture lives in the encrypted profile (PUT /api/e2e/profile).
        if (await db.UserVaults.AnyAsync(v => v.UserId == user.Id))
            return Results.Problem(PlainProfileMessage, statusCode: StatusCodes.Status409Conflict);

        user.Avatar = dto.Data;
        user.AvatarContentType = dto.ContentType;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>Removes the current user's profile picture.</summary>
    public static async Task<IResult> DeleteAvatarHandler(
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        user.Avatar = null;
        user.AvatarContentType = null;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>
    /// Accepts a request to update the current user's profile.
    /// </summary>
    public static async Task<IResult> UpdateProfileHandler(
        [FromBody] UpdateProfileDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] ApplicationDbContext db)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        // With encryption set up, the phone number lives in the encrypted profile; this only changes the user name.
        if (!string.IsNullOrEmpty(dto.PhoneNumber) && await db.UserVaults.AnyAsync(v => v.UserId == user.Id))
            return Results.Problem(PlainProfileMessage, statusCode: StatusCodes.Status409Conflict);
        if (dto.PhoneNumber is not null)
            user.PhoneNumber = dto.PhoneNumber.Length == 0 ? null : dto.PhoneNumber;
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
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] ApplicationDbContext dbContext)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();

        var result = await AccountDeletion.DeleteAsync(dbContext, userManager, user);
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

    /// <summary>Turns on two-factor authentication once the user typed a code from the authenticator app (which
    /// proves the app has the key), and returns the recovery codes.</summary>
    public static async Task<IResult> Enable2FaHandler(
        [FromBody] TwoFactorCodeDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();
        if (user.TwoFactorEnabled)
            return Results.BadRequest("Two-factor authentication is on already.");

        if (!await TwoFactorAuth.VerifyAsync(userManager, user, dto.Code, allowRecoveryCode: false))
            return Results.BadRequest(TwoFactorAuth.InvalidCodeMessage);

        var result = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorAuth.RecoveryCodeCount);
        return Results.Ok(new RecoveryCodesDto(codes?.ToList() ?? []));
    }

    /// <summary>Turns off two-factor authentication (needs a current code or a recovery code) and forgets the key,
    /// so turning it on again needs a new QR code scan.</summary>
    public static async Task<IResult> Disable2FaHandler(
        [FromBody] TwoFactorCodeDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();
        if (!user.TwoFactorEnabled)
            return Results.Ok();

        if (!await TwoFactorAuth.VerifyAsync(userManager, user, dto.Code))
            return Results.BadRequest(TwoFactorAuth.InvalidCodeMessage);

        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (result.Succeeded)
            result = await userManager.ResetAuthenticatorKeyAsync(user);
        return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors);
    }

    /// <summary>Replaces the recovery codes (the old ones stop working).</summary>
    public static async Task<IResult> NewRecoveryCodesHandler(
        [FromBody] TwoFactorCodeDto dto,
        HttpContext context,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user == null) return Results.NotFound();
        if (!user.TwoFactorEnabled)
            return Results.BadRequest("Two-factor authentication is off.");
        if (!await TwoFactorAuth.VerifyAsync(userManager, user, dto.Code, allowRecoveryCode: false))
            return Results.BadRequest(TwoFactorAuth.InvalidCodeMessage);

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorAuth.RecoveryCodeCount);
        return Results.Ok(new RecoveryCodesDto(codes?.ToList() ?? []));
    }

    /// <summary>
    /// Logs out the current user and redirects to the home page.
    /// </summary>
    public static async Task<IResult> WebLogOutHandler(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/");
    }
}
