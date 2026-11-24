using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Web.Api;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

/// <summary>
/// Sign-in with Google, Microsoft or GitHub for the WebAssembly app, which signs in with tokens, not cookies.
/// The provider redirects back to <see cref="CallbackAsync"/>; for an existing account the server hands the
/// browser a short-lived one-time code (in the URL fragment, which is never sent to a server) that the app
/// exchanges for tokens. A new account is only created after the user agreed to the privacy policy.
/// </summary>
public static class ExternalLogins
{
    private const string ExchangePurpose = "Onyx.ExternalLogin.Exchange";
    private const string LinkPurpose = "Onyx.ExternalLogin.Link";
    private const string AppRegisterPurpose = "Onyx.ExternalLogin.AppRegister";
    private static readonly TimeSpan s_CodeLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan s_RegisterLifetime = TimeSpan.FromMinutes(15);

    /// <summary>An external login waiting for an account, for the apps (they have no external cookie).</summary>
    private sealed record AppRegisterTicket(string Provider, string Key, string? DisplayName, string? Email,
        string Challenge);

    public static async Task<IResult> SchemesAsync(SignInManager<ApplicationUser> signInManager) =>
        Results.Ok((await signInManager.GetExternalAuthenticationSchemesAsync())
            .Select(s => new ExternalSchemeDto(s.Name, s.DisplayName ?? s.Name)));

    public static async Task<IResult> StartAsync(HttpContext context, SignInManager<ApplicationUser> signInManager,
        [FromForm] string provider, [FromForm] string? returnUrl)
    {
        if (!await IsSchemeAsync(signInManager, provider))
            return Results.BadRequest("Unknown login provider.");

        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var redirect = $"/account/external-callback?returnUrl={Uri.EscapeDataString(ReturnUrl.Sanitize(returnUrl))}";
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirect);
        return Results.Challenge(properties, [provider]);
    }

    /// <summary>Starts an external sign-in from a native app in the system browser. It ends at
    /// <see cref="AppExternalLogin.CallbackUrl"/>, which hands the result to the app.</summary>
    public static async Task<IResult> StartAppAsync(HttpContext context, SignInManager<ApplicationUser> signInManager,
        [FromQuery] string provider, [FromQuery] string challenge)
    {
        if (!await IsSchemeAsync(signInManager, provider))
            return Results.BadRequest("Unknown login provider.");
        if (string.IsNullOrEmpty(challenge) || challenge.Length > 64 || !challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return Results.BadRequest("Invalid challenge.");

        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider,
            $"/account/external-callback?app={challenge}");
        return Results.Challenge(properties, [provider]);
    }

    /// <param name="app">Set for sign-ins started by <see cref="StartAppAsync"/>: the app's challenge.</param>
    public static async Task<IResult> CallbackAsync(HttpContext context,
        SignInManager<ApplicationUser> signInManager, IDataProtectionProvider dataProtection,
        [FromQuery] string? returnUrl, [FromQuery] string? remoteError, [FromQuery] string? app)
    {
        // The web app reads the result from the fragment of its own page; an app gets it through its callback URL.
        IResult Done(string fragment) => app is null
            ? Results.LocalRedirect($"/account/external#{fragment}")
            : Results.Redirect($"{AppExternalLogin.CallbackUrl}#{fragment}");

        var target = Uri.EscapeDataString(ReturnUrl.Sanitize(returnUrl));
        if (remoteError is not null)
            return Done($"error={Uri.EscapeDataString(remoteError)}");

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return Done("error=" + Uri.EscapeDataString("The sign-in was not completed."));

        var user = await signInManager.UserManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user is null)
        {
            if (app is null)
            {
                // Keep the external cookie: the register step needs it.
                return Done($"register=1&returnUrl={target}");
            }

            // The app's web view doesn't share the browser's cookies, so the login travels in a ticket.
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            var ticket = dataProtection.CreateProtector(AppRegisterPurpose).ToTimeLimitedDataProtector().Protect(
                System.Text.Json.JsonSerializer.Serialize(new AppRegisterTicket(info.LoginProvider, info.ProviderKey,
                    info.ProviderDisplayName, info.Principal.FindFirstValue(ClaimTypes.Email), app)),
                s_RegisterLifetime);
            return Done($"register=1&ticket={Uri.EscapeDataString(ticket)}");
        }

        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var code = dataProtection.CreateProtector(ExchangePurpose).ToTimeLimitedDataProtector()
            .Protect($"{user.Id}|{user.SecurityStamp}|{app}", s_CodeLifetime);
        return Done($"code={Uri.EscapeDataString(code)}&returnUrl={target}");
    }

    /// <summary>Whether <paramref name="verifier"/> belongs to <paramref name="challenge"/>; true for sign-ins
    /// from the web app, which have no challenge.</summary>
    private static bool VerifierMatches(string challenge, string? verifier) =>
        challenge.Length == 0 || (verifier is not null && System.Security.Cryptography.CryptographicOperations
            .FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(AppExternalLogin.Challenge(verifier)),
                System.Text.Encoding.ASCII.GetBytes(challenge)));

    private static AppRegisterTicket? ReadTicket(IDataProtectionProvider dataProtection, string ticket)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AppRegisterTicket>(
                dataProtection.CreateProtector(AppRegisterPurpose).ToTimeLimitedDataProtector().Unprotect(ticket));
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Trades the one-time code from <see cref="CallbackAsync"/> for tokens.</summary>
    public static async Task<IResult> ExchangeAsync([FromBody] ExternalExchangeDto dto,
        UserManager<ApplicationUser> userManager, IDataProtectionProvider dataProtection, KeyAccessor keyAccessor,
        IMemoryCache usedCodes)
    {
        string payload;
        try
        {
            payload = dataProtection.CreateProtector(ExchangePurpose).ToTimeLimitedDataProtector().Unprotect(dto.Code);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Results.BadRequest("The sign-in has expired. Please try again.");
        }

        // Each code works once.
        if (usedCodes.TryGetValue(dto.Code, out _))
            return Results.BadRequest("The sign-in has expired. Please try again.");

        var parts = payload.Split('|', 3);
        var user = await userManager.FindByIdAsync(parts[0]);
        if (user is null || user.SecurityStamp != parts[1] || !VerifierMatches(parts.Length > 2 ? parts[2] : "", dto.Verifier))
            return Results.BadRequest("The sign-in has expired. Please try again.");

        // Accounts with two-factor authentication need a code here too. The one-time code stays valid until it
        // is used, so the app can ask for the code and send both again.
        if (await AuthEndpoints.TwoFactorCheckAsync(userManager, user, dto.TwoFactorCode) is { } twoFactor)
            return twoFactor;

        usedCodes.Set(dto.Code, true, s_CodeLifetime);
        return Results.Ok(AuthEndpoints.IssueTokens(user, keyAccessor));
    }

    /// <summary>The external sign-in waiting for an account: provider and email address.</summary>
    public static async Task<IResult> PendingAsync(SignInManager<ApplicationUser> signInManager,
        IDataProtectionProvider dataProtection, [FromQuery] string? ticket)
    {
        if (ticket is not null)
        {
            return ReadTicket(dataProtection, ticket) is { } pending
                ? Results.Ok(new ExternalPendingDto(pending.DisplayName ?? pending.Provider, pending.Email))
                : Results.NotFound();
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        return info is null
            ? Results.NotFound()
            : Results.Ok(new ExternalPendingDto(info.ProviderDisplayName ?? info.LoginProvider,
                info.Principal.FindFirstValue(ClaimTypes.Email)));
    }

    /// <summary>Creates an account for the pending external sign-in, once the user agreed to the privacy policy.</summary>
    public static async Task<IResult> RegisterAsync(HttpContext context, [FromBody] ExternalRegisterDto dto,
        SignInManager<ApplicationUser> signInManager, IEmailSender<ApplicationUser> emailSender,
        IOptions<OnyxServerOptions> serverOptions, KeyAccessor keyAccessor, TimeProvider time,
        IDataProtectionProvider dataProtection)
    {
        if (!serverOptions.Value.AllowRegistration)
            return Results.Problem(OnyxServerOptions.RegistrationClosedMessage, statusCode: StatusCodes.Status403Forbidden);
        if (!dto.AcceptPrivacyPolicy)
            return Results.Problem(AuthEndpoints.ConsentRequiredMessage, statusCode: StatusCodes.Status400BadRequest);

        UserLoginInfo? info;
        string? email;
        if (dto.Ticket is not null)
        {
            var ticket = ReadTicket(dataProtection, dto.Ticket);
            if (ticket is null || !VerifierMatches(ticket.Challenge, dto.Verifier))
                return Results.BadRequest("The sign-in has expired. Please try again.");
            info = new UserLoginInfo(ticket.Provider, ticket.Key, ticket.DisplayName);
            email = ticket.Email;
        }
        else
        {
            var external = await signInManager.GetExternalLoginInfoAsync();
            info = external;
            email = external?.Principal.FindFirstValue(ClaimTypes.Email);
        }

        if (info is null)
            return Results.BadRequest("The sign-in has expired. Please try again.");
        if (string.IsNullOrWhiteSpace(email))
            return Results.BadRequest($"{info.ProviderDisplayName} did not share an email address.");

        var users = signInManager.UserManager;
        if (await users.FindByEmailAsync(email) is not null)
            return Results.Conflict(
                $"An account with {email} exists already. Sign in with its password, then link " +
                $"{info.ProviderDisplayName} under Account → External logins.");

        var now = time.GetUtcNow().UtcDateTime;
        var user = new ApplicationUser
        {
            UserName = await FreeUserNameAsync(users, email),
            Email = email,
            CreatedAt = now,
            PrivacyConsentAt = now,
            PrivacyPolicyVersion = AuthEndpoints.Truncate(dto.PrivacyPolicyVersion, 20),
        };

        var result = await users.CreateAsync(user);
        if (result.Succeeded)
            result = await users.AddLoginAsync(user, info);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        await AuthEndpoints.SendConfirmationAsync(context, users, emailSender, user);
        return Results.Ok(AuthEndpoints.IssueTokens(user, keyAccessor));
    }

    public static async Task<IResult> ListAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
            return Results.NotFound();
        return Results.Ok((await userManager.GetLoginsAsync(user))
            .Select(l => new ExternalLoginDto(l.LoginProvider, l.ProviderDisplayName ?? l.LoginProvider)));
    }

    public static async Task<IResult> RemoveAsync([FromRoute] string provider, HttpContext context,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
            return Results.NotFound();

        var logins = await userManager.GetLoginsAsync(user);
        var login = logins.FirstOrDefault(l => l.LoginProvider == provider);
        if (login is null)
            return Results.NotFound();
        if (logins.Count == 1 && !await userManager.HasPasswordAsync(user))
            return Results.BadRequest("Set a password first, or you could no longer sign in.");

        var result = await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        return result.Succeeded ? Results.NoContent() : Results.BadRequest(result.Errors);
    }

    /// <summary>A ticket for <see cref="StartLinkAsync"/>: the browser posts a form, which carries no token.</summary>
    public static async Task<IResult> LinkTicketAsync(HttpContext context, UserManager<ApplicationUser> userManager,
        IDataProtectionProvider dataProtection)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
            return Results.NotFound();
        return Results.Ok(new LinkTicketDto(dataProtection.CreateProtector(LinkPurpose).ToTimeLimitedDataProtector()
            .Protect(user.Id, s_CodeLifetime)));
    }

    public static async Task<IResult> StartLinkAsync(HttpContext context, SignInManager<ApplicationUser> signInManager,
        IDataProtectionProvider dataProtection, [FromForm] string provider, [FromForm] string ticket)
    {
        string userId;
        try
        {
            userId = dataProtection.CreateProtector(LinkPurpose).ToTimeLimitedDataProtector().Unprotect(ticket);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Results.LocalRedirect("/account/manage/external#error=" + Uri.EscapeDataString("Please try again."));
        }

        if (!await IsSchemeAsync(signInManager, provider))
            return Results.BadRequest("Unknown login provider.");

        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        // The user id travels as the XSRF value and is checked when the provider redirects back.
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider,
            "/account/link-external-callback", userId);
        return Results.Challenge(properties, [provider]);
    }

    public static async Task<IResult> LinkCallbackAsync(HttpContext context,
        SignInManager<ApplicationUser> signInManager)
    {
        const string page = "/account/manage/external";
        var external = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (external.Properties?.Items.TryGetValue("XsrfId", out var userId) != true || userId is null)
            return Results.LocalRedirect($"{page}#error={Uri.EscapeDataString("The login was not linked.")}");

        var info = await signInManager.GetExternalLoginInfoAsync(userId);
        var user = await signInManager.UserManager.FindByIdAsync(userId);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        if (info is null || user is null)
            return Results.LocalRedirect($"{page}#error={Uri.EscapeDataString("The login was not linked.")}");

        var result = await signInManager.UserManager.AddLoginAsync(user, info);
        return Results.LocalRedirect(result.Succeeded
            ? $"{page}#status=linked"
            : $"{page}#error={Uri.EscapeDataString("This login belongs to another account already.")}");
    }

    private static async Task<bool> IsSchemeAsync(SignInManager<ApplicationUser> signInManager, string provider) =>
        (await signInManager.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == provider);

    /// <summary>A user name from the email address (letters and digits only), made unique with a number.</summary>
    public static async Task<string> FreeUserNameAsync(UserManager<ApplicationUser> users, string email)
    {
        var name = new string(email.Split('@')[0].Where(char.IsAsciiLetterOrDigit).ToArray());
        if (name.Length == 0)
            name = "user";
        var candidate = name;
        for (var i = 2; await users.FindByNameAsync(candidate) is not null; i++)
            candidate = $"{name}{i}";
        return candidate;
    }
}
