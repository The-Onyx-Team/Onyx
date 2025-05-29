namespace Onyx.App.Web.Services.Mail;

public class IdentitySmtpEmailSender(SmtpClient smtpClient) : IEmailSender<ApplicationUser>
{
    private const string SenderMailAddress = "no-reply@onyx.g-martin.work";
    
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        using var message = new MailMessage(SenderMailAddress, email);
        message.Subject = "";
        message.BodyEncoding = Encoding.UTF8;
        message.IsBodyHtml = true;
        message.Body = $@"
            <div style='
                font-family: Arial, sans-serif;
                background-color: #3A3A3A;
                color: #A1A1A1;
                padding: 40px;
                border-radius: 10px;
                max-width: 600px;
                margin: auto;
            '>
                <!-- Header mit Logo -->
                <div style='display: flex; align-items: center; border-bottom: 2px solid #000000; padding-bottom: 20px;'>
                    <img src='cid:logo' alt='Onyx Logo' style='height: 50px; margin-right: 15px;'/>
                    <h1 style='color: #F5F1EB; font-size: 32px; margin: 0;'>Onyx</h1>
                </div>

                <!-- Inhalt -->
                <div style='margin-top: 30px;'>
                    <p style='font-size: 18px;'>Willkommen bei Onyx!</p>
                    <p style='font-size: 16px; color: #E0E0E0;'>Bitte bestätige deine E-Mail-Adresse, indem du auf den folgenden Button klickst:</p>

                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='{confirmationLink}' style='
                            background-color: #E37A37;
                            color: #000000;
                            text-decoration: none;
                            padding: 12px 24px;
                            border-radius: 8px;
                            font-weight: bold;
                            font-size: 16px;
                            display: inline-block;
                        '>Account bestätigen</a>
                    </div>

                    <p style='font-size: 14px; color: #838383;'>Wenn du dich nicht bei Onyx registriert hast, kannst du diese E-Mail ignorieren.</p>
                </div>

                <!-- Footer -->
                <div style='
                    margin-top: 40px;
                    padding-top: 20px;
                    border-top: 1px solid #000000;
                    font-size: 13px;
                    color: #838383;
                    text-align: center;
                '>
                    <p style='margin-bottom: 10px;'>
                        <a href='https://deine-domain.de/datenschutz' style='color: #E37A37; text-decoration: none;'>Datenschutz</a> |
                        <a href='https://deine-domain.de/support' style='color: #E37A37; text-decoration: none;'>Support</a>
                    </p>
                    <p style='margin: 0;'>© {DateTime.Now.Year} Onyx. Alle Rechte vorbehalten.</p>
                </div>
            </div>";

        
        var logoPath = "./wwwroot/img/Icon.png";
        var inlineLogo = new LinkedResource(logoPath)
        {
            ContentId = "logo"
        };

        var htmlView = AlternateView.CreateAlternateViewFromString(message.Body, Encoding.UTF8, MediaTypeNames.Text.Html);
        htmlView.LinkedResources.Add(inlineLogo);
        message.AlternateViews.Add(htmlView);

        await smtpClient.SendMailAsync(message);
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        using var message = new MailMessage(SenderMailAddress, email);
        message.Subject = "";
        message.BodyEncoding = Encoding.UTF8;
        message.IsBodyHtml = true;
        message.Body = $@"
            <div style='
                font-family: Arial, sans-serif;
                background-color: #3A3A3A;
                color: #A1A1A1;
                padding: 40px;
                border-radius: 10px;
                max-width: 600px;
                margin: auto;
            '>
                <div style='display: flex; align-items: center; border-bottom: 2px solid #000000; padding-bottom: 20px;'>
                    <img src='cid:logo' alt='Onyx Logo' style='height: 50px; margin-right: 15px;'/>
                    <h1 style='color: #F5F1EB; font-size: 32px; margin: 0;'>Onyx</h1>
                </div>
                
                <div style='margin-top: 30px;'>
                    <p style='font-size: 18px;'>Willkommen bei Onyx!</p>
                    <p style='font-size: 16px; color: #E0E0E0;'>Setze dein Passwort zurück, indem du auf den folgenden Button klickst:</p>
                    
                    <div style='text-align: center; margin: 30px 0;'>
                        <a href='{resetLink}' style='
                            background-color: #E37A37;
                            color: #000000;
                            text-decoration: none;
                            padding: 12px 24px;
                            border-radius: 8px;
                            font-weight: bold;
                            font-size: 16px;
                        '>Reset Password</a>
                    </div>

                    <p style='font-size: 14px; color: #838383;'>Wenn du dein Passwort nicht zurücksetzen willst, kannst du diese E-Mail ignorieren.</p>
                </div>

                <!-- Footer -->
                <div style='
                    margin-top: 40px;
                    padding-top: 20px;
                    border-top: 1px solid #000000;
                    font-size: 13px;
                    color: #838383;
                    text-align: center;
                '>
                    <p style='margin-bottom: 10px;'>
                        <a href='https://deine-domain.de/datenschutz' style='color: #E37A37; text-decoration: none;'>Datenschutz</a> |
                        <a href='https://deine-domain.de/support' style='color: #E37A37; text-decoration: none;'>Support</a>
                    </p>
                    <p style='margin: 0;'>© {DateTime.Now.Year} Onyx. Alle Rechte vorbehalten.</p>
                </div>
            </div>";
        
        var logoPath = "./wwwroot/img/Icon.png";
        var inlineLogo = new LinkedResource(logoPath)
        {
            ContentId = "logo"
        };

        var htmlView = AlternateView.CreateAlternateViewFromString(message.Body, Encoding.UTF8, MediaTypeNames.Text.Html);
        htmlView.LinkedResources.Add(inlineLogo);
        message.AlternateViews.Add(htmlView);

        await smtpClient.SendMailAsync(message);
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        using var message = new MailMessage(SenderMailAddress, email);
        message.Subject = "";
        message.BodyEncoding = Encoding.UTF8;
        message.IsBodyHtml = true;
        message.Body = $"Your password reset code is: {resetCode}";

        await smtpClient.SendMailAsync(message);
    }
}