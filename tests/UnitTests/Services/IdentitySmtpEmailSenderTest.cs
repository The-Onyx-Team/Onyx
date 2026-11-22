using System.Net;
using JetBrains.Annotations;
using Onyx.App.Web.Services.Mail;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

[TestSubject(typeof(IdentitySmtpEmailSender))]
public class IdentitySmtpEmailSenderTest
{
    [Fact]
    public void CreateClient_WithSmtpsAndLogin_UsesStartTlsAndDecodedCredentials()
    {
        using var client = IdentitySmtpEmailSender.CreateClient(
            new Uri("smtps://onyx%40example.com:p%40ss%2Fw%3Ard@mail.smtp2go.com:2525"));

        client.Host.ShouldBe("mail.smtp2go.com");
        client.Port.ShouldBe(2525);
        client.EnableSsl.ShouldBeTrue();
        var credential = client.Credentials.ShouldBeOfType<NetworkCredential>();
        credential.UserName.ShouldBe("onyx@example.com");
        credential.Password.ShouldBe("p@ss/w:rd");
    }

    [Fact]
    public void CreateClient_WithPlainSmtp_HasNoTlsAndNoCredentials()
    {
        using var client = IdentitySmtpEmailSender.CreateClient(new Uri("smtp://localhost:1025"));

        client.Host.ShouldBe("localhost");
        client.Port.ShouldBe(1025);
        client.EnableSsl.ShouldBeFalse();
        client.Credentials.ShouldBeNull();
    }
}
