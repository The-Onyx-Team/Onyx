using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

/// <summary>Two-factor authentication with an authenticator app (TOTP, RFC 6238) and one-time recovery codes.</summary>
public static class TwoFactorAuth
{
    public const string Issuer = "Onyx";
    public const int RecoveryCodeCount = 10;

    public const string RequiredMessage = "Enter the code from your authenticator app.";
    public const string InvalidCodeMessage = "The code is not valid. Check the time on your phone, or use a recovery code.";

    /// <summary>Checks a code from the authenticator app (6 digits) or, if <paramref name="allowRecoveryCode"/>,
    /// redeems a recovery code (it then can't be used again).</summary>
    public static async Task<bool> VerifyAsync(UserManager<ApplicationUser> users, ApplicationUser user, string? code,
        bool allowRecoveryCode = true)
    {
        var cleaned = (code ?? "").Replace(" ", "").Replace("-", "").Trim();
        if (cleaned.Length == 0)
            return false;

        if (cleaned.Length == 6 && cleaned.All(char.IsAsciiDigit))
            return await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, cleaned);

        // Recovery codes are stored as generated ("XXXXX-XXXXX", compared exactly); users may type them without
        // the hyphen or in lower case.
        if (!allowRecoveryCode || cleaned.Length != 10)
            return false;
        var recovery = $"{cleaned[..5]}-{cleaned[5..]}";
        return (await users.RedeemTwoFactorRecoveryCodeAsync(user, recovery.ToUpperInvariant())).Succeeded
               || (await users.RedeemTwoFactorRecoveryCodeAsync(user, recovery.ToLowerInvariant())).Succeeded;
    }

    /// <summary>The user's authenticator key, created on first use.</summary>
    public static async Task<string> KeyAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrEmpty(key))
            return key;
        await users.ResetAuthenticatorKeyAsync(user);
        return (await users.GetAuthenticatorKeyAsync(user))!;
    }

    /// <summary>The key in lower-case groups of four, easier to type.</summary>
    public static string FormatKey(string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (result.Length > 0)
                result.Append(' ');
            result.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return result.ToString().ToLowerInvariant();
    }

    /// <summary>The <c>otpauth://</c> address for the QR code (Google Authenticator key URI format).</summary>
    public static string Uri(string account, string key) =>
        string.Format(CultureInfo.InvariantCulture, "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            System.Uri.EscapeDataString(Issuer), System.Uri.EscapeDataString(account), key);
}
