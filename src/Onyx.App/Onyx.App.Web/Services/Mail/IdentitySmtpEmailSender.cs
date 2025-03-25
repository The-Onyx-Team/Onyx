using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Mail;

public class IdentitySmtpEmailSender(SmtpClient smtpClient) : IEmailSender<ApplicationUser>
{
    private const string SenderMailAddress = "no-reply@onyx.at";
    
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        using var message = new MailMessage(SenderMailAddress, email);
        message.Subject = "";
        message.Body = $"Click <a href=\"{confirmationLink}\">here</a> to confirm your account!";

        await smtpClient.SendMailAsync(message);
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        using var message = new MailMessage(SenderMailAddress, email);
        message.Subject = "";
        message.Body = $"Click <a href=\"{resetLink}\">here</a> to reset your password!";

        await smtpClient.SendMailAsync(message);
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        using var message = new MailMessage(SenderMailAddress, email);
        message.Subject = "";
        message.Body = $"Your password reset code is: {resetCode}";

        await smtpClient.SendMailAsync(message);
    }
}