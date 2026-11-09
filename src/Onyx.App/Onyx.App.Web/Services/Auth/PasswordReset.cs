using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

/// <summary>Password reset emails, shared by the web UI and the API used by the app.</summary>
public static class PasswordReset
{
    /// <summary>
    /// Sends a reset link if the address belongs to an account. Does nothing otherwise, so callers can answer the
    /// same way in both cases and not reveal which addresses are registered.
    /// </summary>
    /// <param name="baseUrl">Public base URL of the web app, e.g. <c>https://onyx.example.com/</c>.</param>
    public static async Task SendLinkAsync(UserManager<ApplicationUser> userManager,
        IEmailSender<ApplicationUser> emailSender, string email, string baseUrl)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return;

        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = QueryHelpers.AddQueryString(new Uri(new Uri(baseUrl), "/account/resetPasswordWithEmail").AbsoluteUri,
            new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });

        await emailSender.SendPasswordResetLinkAsync(user, email, HtmlEncoder.Default.Encode(link));
    }
}
