using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Onyx.App.Web.Api;
using Onyx.App.Web.Services;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Shouldly;
using Xunit;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace UnitTests.Api;

[TestSubject(typeof(AuthEndpoints))]
public class AuthEndpointsTest
{
    private readonly Mock<SignInManager<ApplicationUser>> m_SignInManager;
    private readonly Mock<UserManager<ApplicationUser>> m_UserManager;
    private readonly Mock<IUserClaimsPrincipalFactory<ApplicationUser>> m_ClaimsFactory;
    private readonly Mock<HttpContext> m_HttpContext;
    private readonly KeyAccessor m_KeyAccessor;
    private readonly ApplicationUser m_TestUser;

    public AuthEndpointsTest()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var passwordHasher = new Mock<IPasswordHasher<ApplicationUser>>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var keyNormalizer = new Mock<ILookupNormalizer>();
        var errors = new Mock<IdentityErrorDescriber>();
        var authServiceProvider = new Mock<IAuthenticationService>();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();

        services
            .Setup(x => x.GetService(typeof(IAuthenticationService)))
            .Returns(authServiceProvider.Object);

        m_UserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object,
            options.Object,
            passwordHasher.Object,
            userValidators,
            passwordValidators,
            keyNormalizer.Object,
            errors.Object,
            services.Object,
            logger.Object);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var loggerSignIn = new Mock<ILogger<SignInManager<ApplicationUser>>>();
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var confirmation = new Mock<IUserConfirmation<ApplicationUser>>();

        m_SignInManager = new Mock<SignInManager<ApplicationUser>>(
            m_UserManager.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            options.Object,
            loggerSignIn.Object,
            schemes.Object,
            confirmation.Object);

        m_KeyAccessor = new KeyAccessor(new RsaSecurityKey(RSA.Create()));
        m_ClaimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        m_HttpContext = new Mock<HttpContext>();
        m_HttpContext.Setup(x => x.RequestServices)
            .Returns(services.Object);

        authServiceProvider
            .Setup(x => x.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        m_TestUser = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            EmailConfirmed = true,
            Email = "test@example.com",
            NormalizedEmail = "TEST@EXAMPLE.COM",
            NormalizedUserName = "TESTUSER",
            UserName = "testuser"
        };

        m_UserManager.Setup(x => x.FindByIdAsync(m_TestUser.Id))
            .ReturnsAsync(m_TestUser);
    }

    /// <summary>Empty in-memory SQLite database (account deletion runs a bulk delete, which mocks can't do).</summary>
    private static ApplicationDbContext CreateDbContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnUnauthorized_WhenUserIsNull()
    {
        // Arrange
        var request = new RefreshTokenDto(Token: "");

        // Act
        var result = await AuthEndpoints.RefreshHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnOk_WithNewToken()
    {
        // Arrange
        var refreshToken = JwtTools.GenerateRefreshToken(m_KeyAccessor.ApplicationKey, m_TestUser.Id,
            m_TestUser.Email!, m_TestUser.SecurityStamp);
        var request = new RefreshTokenDto(Token: refreshToken);

        // Act
        var result = await AuthEndpoints.RefreshHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldNotBeOfType<UnauthorizedHttpResult>();
        result.GetType().Name.ShouldStartWith("Ok");

        var resultType = result.GetType();
        var valueProperty = resultType.GetProperty("Value");
        valueProperty.ShouldNotBeNull();

        var value = valueProperty.GetValue(result);
        value.ShouldNotBeNull();

        var tokenProperty = value.GetType().GetProperty("Token",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);
        tokenProperty.ShouldNotBeNull();

        var token = tokenProperty.GetValue(value) as string;
        token.ShouldNotBeNull();

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);
        jwtToken.Claims.ShouldNotBeNull();
        jwtToken.Claims.ShouldNotBeEmpty();
        jwtToken.Claims.ShouldContain(x =>
            (x.Type == ClaimTypes.NameIdentifier || x.Type == "nameid") && x.Value == m_TestUser.Id);
        jwtToken.Claims.ShouldContain(x =>
            (x.Type == ClaimTypes.Name || x.Type == "unique_name") && x.Value == m_TestUser.Email);
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnUnauthorized_WhenTokenIsExpired()
    {
        // Arrange
        var handler = new JwtSecurityTokenHandler();

        var claims = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, m_TestUser.Id),
            new Claim(ClaimTypes.Name, m_TestUser.Email!),
        ]);

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = claims,
            Expires = DateTime.UtcNow.AddYears(-1),
            NotBefore = DateTime.UtcNow.AddYears(-2),
            SigningCredentials = new SigningCredentials(m_KeyAccessor.ApplicationKey, SecurityAlgorithms.RsaSha256)
        });

        var request = new RefreshTokenDto(Token: handler.WriteToken(token));

        // Act
        var result = await AuthEndpoints.RefreshHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnUnauthorized_WhenTokenIsUnsigned()
    {
        // Arrange: a forged token with alg "none" for a real user id. The handler used to accept these.
        static string Encode(string json) => Base64UrlEncoder.Encode(json);
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var forged = Encode("""{"alg":"none","typ":"JWT"}""") + "." +
                     Encode($$"""{"nameid":"{{m_TestUser.Id}}","unique_name":"{{m_TestUser.Email}}","token_use":"refresh","sstamp":"{{m_TestUser.SecurityStamp}}","exp":{{exp}}}""") + ".";

        // Act
        var result = await AuthEndpoints.RefreshHandler(new RefreshTokenDto(forged), m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnUnauthorized_WhenSignedWithAnotherKey()
    {
        // Arrange
        var otherKey = new RsaSecurityKey(RSA.Create());
        var token = JwtTools.GenerateRefreshToken(otherKey, m_TestUser.Id, m_TestUser.Email!, m_TestUser.SecurityStamp);

        // Act
        var result = await AuthEndpoints.RefreshHandler(new RefreshTokenDto(token), m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnUnauthorized_WhenGivenAnAccessToken()
    {
        // Arrange
        var token = JwtTools.GenerateToken(m_KeyAccessor.ApplicationKey, m_TestUser.Id, m_TestUser.Email!,
            TimeSpan.FromHours(1));

        // Act
        var result = await AuthEndpoints.RefreshHandler(new RefreshTokenDto(token), m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnUnauthorized_WhenSecurityStampChanged()
    {
        // Arrange: the token was issued before a password change rotated the stamp.
        var token = JwtTools.GenerateRefreshToken(m_KeyAccessor.ApplicationKey, m_TestUser.Id, m_TestUser.Email!,
            "stamp-before-password-change");

        // Act
        var result = await AuthEndpoints.RefreshHandler(new RefreshTokenDto(token), m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task ForgotPasswordHandler_UnknownEmail_AnswersOkAndSendsNothing()
    {
        // Arrange
        var emailSender = new Mock<IEmailSender<ApplicationUser>>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("onyx.example");

        // Act
        var result = await AuthEndpoints.ForgotPasswordHandler(new ForgotPasswordDto("nobody@example.com"), context,
            m_UserManager.Object, emailSender.Object);

        // Assert
        result.GetType().Name.ShouldStartWith("Ok");
        emailSender.VerifyNoOtherCalls();
        m_UserManager.Verify(x => x.RemovePasswordAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordHandler_KnownEmail_SendsLinkWithCode_AndKeepsPassword()
    {
        // Arrange
        var emailSender = new Mock<IEmailSender<ApplicationUser>>();
        m_UserManager.Setup(x => x.FindByEmailAsync(m_TestUser.Email!)).ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.GeneratePasswordResetTokenAsync(m_TestUser)).ReturnsAsync("reset+code/1");
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("onyx.example");

        // Act
        await AuthEndpoints.ForgotPasswordHandler(new ForgotPasswordDto(m_TestUser.Email!), context,
            m_UserManager.Object, emailSender.Object);

        // Assert
        emailSender.Verify(x => x.SendPasswordResetLinkAsync(m_TestUser, m_TestUser.Email!,
            It.Is<string>(link => link.Contains("https://onyx.example/account/resetPasswordWithEmail")
                                  && link.Contains("code=reset%2Bcode%2F1"))), Times.Once);
        m_UserManager.Verify(x => x.RemovePasswordAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task RefreshHandler_ShouldReturnBadRequest_WhenTokenIsInvalid()
    {
        // Arrange
        var request = new RefreshTokenDto(Token: "invalid_token");

        // Act
        var result = await AuthEndpoints.RefreshHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task LoginHandler_WithValidCredentials_ReturnsOkWithTokens()
    {
        // Arrange
        var request = new LoginDto(Email: "test@example.com", Password: "Password123!");

        m_UserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.CheckPasswordAsync(m_TestUser, request.Password))
            .ReturnsAsync(true);

        // Act
        var result = await AuthEndpoints.LoginHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldNotBeOfType<UnauthorizedResult>();

        var resultType = result.GetType();
        var valueProperty = resultType.GetProperty("Value");
        valueProperty.ShouldNotBeNull();

        var value = valueProperty.GetValue(result);
        value.ShouldNotBeNull();

        var tokenProperty = value.GetType().GetProperty("token",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);
        tokenProperty.ShouldNotBeNull();

        var refreshTokenProperty = value.GetType().GetProperty("refreshToken",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);
        refreshTokenProperty.ShouldNotBeNull();

        var token = tokenProperty.GetValue(value) as string;
        token.ShouldNotBeNull();

        var refreshToken = refreshTokenProperty.GetValue(value) as string;
        refreshToken.ShouldNotBeNull();
    }

    [Fact]
    public async Task LoginHandler_WithNonExistentUser_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginDto(Email: "test@example.com", Password: "Password123!");

        m_UserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.LoginHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task LoginHandler_WithIncorrectPassword_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginDto(Email: "test@example.com", Password: "Password123!Wrong");

        m_UserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.CheckPasswordAsync(m_TestUser, request.Password))
            .ReturnsAsync(false);

        // Act
        var result = await AuthEndpoints.LoginHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        result.ShouldBeOfType<BadRequest<string>>();
    }

    private static readonly IOptions<OnyxServerOptions> s_OpenRegistration = Options.Create(new OnyxServerOptions());

    private static DefaultHttpContext RequestContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("onyx.example.com");
        return context;
    }

    private Task<IResult> RegisterAsync(RegisterDto request, IOptions<OnyxServerOptions> options,
        Mock<IEmailSender<ApplicationUser>>? emailSender = null) =>
        AuthEndpoints.RegisterHandler(request, RequestContext(), m_UserManager.Object,
            (emailSender ?? new Mock<IEmailSender<ApplicationUser>>()).Object, options, TimeProvider.System);

    [Fact]
    public async Task RegisterHandler_WhenRegistrationIsDisabled_ReturnsForbiddenWithoutCreatingUser()
    {
        var request = new RegisterDto("newuser", "newuser@example.com", "ValidPassword123!", AcceptPrivacyPolicy: true);
        var closed = Options.Create(new OnyxServerOptions { AllowRegistration = false });

        var result = await RegisterAsync(request, closed);

        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        m_UserManager.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterHandler_WithoutConsent_ReturnsBadRequestWithoutCreatingUser()
    {
        var request = new RegisterDto("newuser", "newuser@example.com", "ValidPassword123!");

        var result = await RegisterAsync(request, s_OpenRegistration);

        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        m_UserManager.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterHandler_WithValidData_RecordsConsentAndSendsConfirmation()
    {
        var request = new RegisterDto("newuser", "newuser@example.com", "ValidPassword123!", true, "2026-11-23");
        m_UserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);
        m_UserManager.Setup(x => x.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync("code+/=");
        var emailSender = new Mock<IEmailSender<ApplicationUser>>();

        var result = await RegisterAsync(request, s_OpenRegistration, emailSender);

        result.ShouldBeOfType<Ok<object>>();
        m_UserManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u => u.Email == request.Email && u.UserName == request.Name
                                        && u.PrivacyConsentAt != null && u.PrivacyPolicyVersion == "2026-11-23"
                                        && u.CreatedAt != null),
            request.Password), Times.Once);
        emailSender.Verify(x => x.SendConfirmationLinkAsync(It.IsAny<ApplicationUser>(), request.Email,
            It.Is<string>(link => link.StartsWith("https://onyx.example.com/account/confirmEmail?userId=")
                                  && link.Contains("code=code%2B%2F%3D"))), Times.Once);
    }

    [Fact]
    public async Task RegisterHandler_WithInvalidData_ReturnsBadRequestWithErrors()
    {
        var request = new RegisterDto("newuser", "newuser@example.com", "weak", AcceptPrivacyPolicy: true);
        var identityErrors = new List<IdentityError>
        {
            new() { Code = "PasswordTooShort", Description = "Password must be at least 6 characters" }
        };
        m_UserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Failed(identityErrors.ToArray()));

        var result = await RegisterAsync(request, s_OpenRegistration);

        result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>().Value.ShouldBe(identityErrors);
    }

    [Fact]
    public async Task ConfirmEmailHandler_WithValidCode_ReturnsOk()
    {
        m_UserManager.Setup(x => x.ConfirmEmailAsync(m_TestUser, "valid-token")).ReturnsAsync(IdentityResult.Success);

        var result = await AuthEndpoints.ConfirmEmailHandler(new ConfirmEmailDto(m_TestUser.Id, "valid-token"),
            m_UserManager.Object);

        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.ConfirmEmailAsync(m_TestUser, "valid-token"), Times.Once);
    }

    [Fact]
    public async Task ConfirmEmailHandler_WithUnknownUserOrBadCode_ReturnsBadRequest()
    {
        m_UserManager.Setup(x => x.FindByIdAsync("unknown")).ReturnsAsync((ApplicationUser)null!);
        m_UserManager.Setup(x => x.ConfirmEmailAsync(m_TestUser, "bad"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));

        (await AuthEndpoints.ConfirmEmailHandler(new ConfirmEmailDto("unknown", "x"), m_UserManager.Object))
            .ShouldBeOfType<BadRequest<string>>();
        (await AuthEndpoints.ConfirmEmailHandler(new ConfirmEmailDto(m_TestUser.Id, "bad"), m_UserManager.Object))
            .ShouldBeOfType<BadRequest<string>>();
    }

    [Fact]
    public async Task ResetPasswordHandler_ChecksTheEmailedCode()
    {
        m_UserManager.Setup(x => x.ResetPasswordAsync(m_TestUser, "code", "NewPassword1"))
            .ReturnsAsync(IdentityResult.Success);
        m_UserManager.Setup(x => x.ResetPasswordAsync(m_TestUser, "wrong", It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));

        (await AuthEndpoints.ResetPasswordHandler(new ResetPasswordDto(m_TestUser.Id, "code", "NewPassword1"),
            m_UserManager.Object)).ShouldBeOfType<Ok>();
        (await AuthEndpoints.ResetPasswordHandler(new ResetPasswordDto(m_TestUser.Id, "wrong", "NewPassword1"),
            m_UserManager.Object)).ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
    }

    [Fact]
    public async Task GetProfileHandler_WithValidUser_ReturnsOkWithProfileData()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);

        // Act
        var result = await AuthEndpoints.GetProfileHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldNotBeOfType<NotFoundResult>();

        // Verify the returned profile data contains the correct properties
        var resultType = result.GetType();
        var valueProperty = resultType.GetProperty("Value");
        valueProperty.ShouldNotBeNull();

        var value = valueProperty.GetValue(result);
        value.ShouldNotBeNull();

        // Verify each profile property exists and matches the test user
        var profileType = value.GetType();

        var emailProp = profileType.GetProperty("Email");
        emailProp.ShouldNotBeNull();
        emailProp.GetValue(value).ShouldBe(m_TestUser.Email);

        var usernameProp = profileType.GetProperty("UserName");
        usernameProp.ShouldNotBeNull();
        usernameProp.GetValue(value).ShouldBe(m_TestUser.UserName);

        profileType.GetProperty("PhoneNumber").ShouldNotBeNull();
        profileType.GetProperty("EmailConfirmed").ShouldNotBeNull();
        profileType.GetProperty("PhoneNumberConfirmed").ShouldNotBeNull();
        profileType.GetProperty("TwoFactorEnabled").ShouldNotBeNull();
    }

    [Fact]
    public async Task GetProfileHandler_WithNoUser_ReturnsNotFound()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.GetProfileHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<NotFound>();
    }

    [Fact]
    public async Task UpdateProfileHandler_WithValidRequest_UpdatesUserAndReturnsOk()
    {
        // Arrange
        var request = new UpdateProfileDto(UserName: "newUsername", PhoneNumber: "1234567890");

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.UpdateProfileHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.UpdateAsync(It.Is<ApplicationUser>(u =>
            u.UserName == request.UserName &&
            u.PhoneNumber == request.PhoneNumber)), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileHandler_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateProfileDto(UserName: "newUsername", PhoneNumber: "1234567890");

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.UpdateProfileHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<NotFound>();
        m_UserManager.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task UpdateProfileHandler_WithInvalidData_ReturnsBadRequestWithErrors()
    {
        // Arrange
        var request = new UpdateProfileDto(UserName: "invalid#username", PhoneNumber: "1234567890");

        var identityErrors = new List<IdentityError>
        {
            new() { Code = "InvalidUserName", Description = "Username contains invalid characters" }
        };
        var failedResult = IdentityResult.Failed(identityErrors.ToArray());

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(failedResult);

        // Act
        var result = await AuthEndpoints.UpdateProfileHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
    }


    [Fact]
    public async Task ChangePasswordHandler_WithValidRequest_ChangesPasswordAndReturnsOk()
    {
        // Arrange
        var request = new ChangePasswordDto(CurrentPassword: "OldPassword123!", NewPassword: "NewPassword456!");

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.ChangePasswordAsync(m_TestUser, request.CurrentPassword, request.NewPassword))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.ChangePasswordHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.ChangePasswordAsync(m_TestUser, request.CurrentPassword, request.NewPassword),
            Times.Once);
    }

    [Fact]
    public async Task ChangePasswordHandler_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var request = new ChangePasswordDto(CurrentPassword: "OldPassword123!", NewPassword: "NewPassword456!");

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.ChangePasswordHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<NotFound>();
        m_UserManager.Verify(x => x.ChangePasswordAsync(It.IsAny<ApplicationUser>(),
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ChangePasswordHandler_WithIncorrectCurrentPassword_ReturnsBadRequestWithErrors()
    {
        // Arrange
        var request = new ChangePasswordDto(CurrentPassword: "WrongPassword!", NewPassword: "NewPassword456!");

        var identityErrors = new List<IdentityError>
        {
            new() { Code = "PasswordMismatch", Description = "Incorrect password" }
        };
        var failedResult = IdentityResult.Failed(identityErrors.ToArray());

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.ChangePasswordAsync(m_TestUser, request.CurrentPassword, request.NewPassword))
            .ReturnsAsync(failedResult);

        // Act
        var result = await AuthEndpoints.ChangePasswordHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
    }

    [Fact]
    public async Task DeleteAccountHandler_WithValidUser_DeletesAccountAndReturnsOk()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.DeleteAsync(m_TestUser))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object, CreateDbContext());

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.DeleteAsync(m_TestUser), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountHandler_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object, CreateDbContext());

        // Assert
        result.ShouldBeOfType<NotFound>();
        m_UserManager.Verify(x => x.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccountHandler_WithDeletionFailure_ReturnsBadRequestWithErrors()
    {
        // Arrange
        var identityErrors = new List<IdentityError>
        {
            new() { Code = "DeletionFailed", Description = "Account cannot be deleted due to existing references" }
        };
        var failedResult = IdentityResult.Failed(identityErrors.ToArray());

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.DeleteAsync(m_TestUser))
            .ReturnsAsync(failedResult);

        // Act
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object, CreateDbContext());

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
    }

    [Fact]
    public async Task WebLogOutHandler_SignsOutUserAndRedirectsToHomePage()
    {
        // Arrange
        var authServiceMock = new Mock<IAuthenticationService>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock
            .Setup(x => x.GetService(typeof(IAuthenticationService)))
            .Returns(authServiceMock.Object);

        // Act
        var result = await AuthEndpoints.WebLogOutHandler(m_SignInManager.Object);

        // Assert
        // Verify sign-out was called
        m_SignInManager.Verify(x => x.SignOutAsync(),
            Times.Once);

        // Verify redirect result
        var redirectResult = result.ShouldBeOfType<RedirectHttpResult>();
        redirectResult.Url.ShouldBe("/");
    }

    [Fact]
    public async Task Enable2FaHandler_WithValidUser_EnablesTwoFactorAndReturnsOk()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.GenerateTwoFactorTokenAsync(m_TestUser, TokenOptions.DefaultPhoneProvider))
            .ReturnsAsync("123456");
        m_UserManager.Setup(x => x.SetTwoFactorEnabledAsync(m_TestUser, true))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.Enable2FaHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.SetTwoFactorEnabledAsync(m_TestUser, true), Times.Once);
    }

    [Fact]
    public async Task WebLogOutHandler_SignsOutUser()
    {
        // Arrange
        var authServiceMock = new Mock<IAuthenticationService>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock
            .Setup(x => x.GetService(typeof(IAuthenticationService)))
            .Returns(authServiceMock.Object);

        // Act
        await AuthEndpoints.WebLogOutHandler(m_SignInManager.Object);

        // Assert
        m_SignInManager.Verify(x => x.SignOutAsync(),
            Times.Once);
    }

    [Fact]
    public async Task RequestEmailChangeHandler_WithValidRequest_GeneratesTokenAndReturnsOk()
    {
        // Arrange
        var request = new ChangeEmailDto("new@example.com");

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.GenerateChangeEmailTokenAsync(m_TestUser, request.NewEmail))
            .ReturnsAsync("token");

        // Act
        var result = await AuthEndpoints.RequestEmailChangeHandler(request, m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.GenerateChangeEmailTokenAsync(m_TestUser, request.NewEmail), Times.Once);
    }

    [Fact]
    public async Task Disable2FaHandler_WithValidUser_DisablesTwoFactorAndReturnsOk()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.SetTwoFactorEnabledAsync(m_TestUser, false))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.Disable2FaHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.SetTwoFactorEnabledAsync(m_TestUser, false), Times.Once);
    }
}