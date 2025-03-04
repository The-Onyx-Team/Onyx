using System.Security.Claims;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Onyx.App.Web.Api;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema.Identity;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

[TestSubject(typeof(AuthEndpoints))]
public class AuthEndpointsTest
{
    private readonly Mock<SignInManager<ApplicationUser>> m_SignInManager;
    private readonly Mock<UserManager<ApplicationUser>> m_UserManager;
    private readonly Mock<IUserClaimsPrincipalFactory<ApplicationUser>> m_ClaimsFactory;
    private readonly Mock<HttpContext> m_HttpContext;
    private readonly KeyAccessor m_KeyAccessor;
    private readonly AuthEndpoints.WebLoginModel m_ValidWebLoginModel;
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
            Email = "test@example.com",
            UserName = "testuser"
        };

        m_ValidWebLoginModel = new AuthEndpoints.WebLoginModel
        {
            Email = "test@example.com",
            Password = "password123",
            Redirect = "/dashboard"
        };
    }

    [Fact]
    public async Task LoginHandler_WithValidCredentials_ShouldRedirect()
    {
        // Arrange
        m_UserManager.Setup(x => x.FindByEmailAsync(m_ValidWebLoginModel.Email))
            .ReturnsAsync(m_TestUser);

        m_SignInManager.Setup(x => x.PasswordSignInAsync(m_TestUser, m_ValidWebLoginModel.Password, true, false))
            .ReturnsAsync(SignInResult.Success);

        m_ClaimsFactory.Setup(x => x.CreateAsync(m_TestUser))
            .ReturnsAsync(new ClaimsPrincipal());

        // Act
        var result = await AuthEndpoints.WebLoginHandler(
            m_ValidWebLoginModel,
            m_SignInManager.Object,
            m_ClaimsFactory.Object,
            m_HttpContext.Object);

        // Assert
        var redirectResult = result.ShouldBeOfType<RedirectHttpResult>();
        redirectResult.Url.ShouldBe(m_ValidWebLoginModel.Redirect);
    }

    [Fact]
    public async Task LoginHandler_WithNonExistentUser_ShouldReturnUnauthorized()
    {
        // Arrange
        m_UserManager.Setup(x => x.FindByEmailAsync(m_ValidWebLoginModel.Email))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.WebLoginHandler(
            m_ValidWebLoginModel,
            m_SignInManager.Object,
            m_ClaimsFactory.Object,
            m_HttpContext.Object);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task LoginHandler_WithInvalidPassword_ShouldReturnUnauthorized()
    {
        // Arrange
        m_UserManager.Setup(x => x.FindByEmailAsync(m_ValidWebLoginModel.Email))
            .ReturnsAsync(m_TestUser);

        m_SignInManager.Setup(x => x.PasswordSignInAsync(m_TestUser, m_ValidWebLoginModel.Password, true, false))
            .ReturnsAsync(SignInResult.Failed);

        // Act
        var result = await AuthEndpoints.WebLoginHandler(
            m_ValidWebLoginModel,
            m_SignInManager.Object,
            m_ClaimsFactory.Object,
            m_HttpContext.Object);

        // Assert
        result.ShouldBeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task LoginHandler_WithValidCredentials_ShouldSignInUser()
    {
        // Arrange
        m_UserManager.Setup(x => x.FindByEmailAsync(m_ValidWebLoginModel.Email))
            .ReturnsAsync(m_TestUser);

        m_SignInManager.Setup(x => x.PasswordSignInAsync(m_TestUser, m_ValidWebLoginModel.Password, true, false))
            .ReturnsAsync(SignInResult.Success);

        var principal = new ClaimsPrincipal();
        m_ClaimsFactory.Setup(x => x.CreateAsync(m_TestUser))
            .ReturnsAsync(principal);

        var authService =
            Mock.Get(
                m_HttpContext.Object.RequestServices.GetService(
                    typeof(IAuthenticationService)) as IAuthenticationService);

        // Act
        await AuthEndpoints.WebLoginHandler(
            m_ValidWebLoginModel,
            m_SignInManager.Object,
            m_ClaimsFactory.Object,
            m_HttpContext.Object);

        // Assert
        authService.Verify(x => x!.SignInAsync(
            m_HttpContext.Object,
            IdentityConstants.ApplicationScheme,
            principal,
            It.IsAny<AuthenticationProperties>()
        ), Times.Once);
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
    public async Task RefreshTokenHandler_WithValidCredentials_ReturnsNewToken()
    {
        // Arrange
        var request = new LoginRequest("test@example.com", "password123");

        m_UserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.CheckPasswordAsync(m_TestUser, request.Password))
            .ReturnsAsync(true);

        // Act
        var result = await AuthEndpoints.RefreshTokenHandler(request, m_UserManager.Object, m_KeyAccessor);

        // Assert
        var okResult = result.ShouldBeOfType<Ok<string>>();
        okResult.Value.ShouldNotBeNull();
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

        m_HttpContext.Setup(x => x.RequestServices)
            .Returns(serviceProviderMock.Object);

        // Act
        await AuthEndpoints.WebLogOutHandler(m_HttpContext.Object);

        // Assert
        authServiceMock.Verify(x => x.SignOutAsync(
                m_HttpContext.Object,
                IdentityConstants.ApplicationScheme,
                It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmEmailHandler_WithValidToken_ReturnsOk()
    {
        // Arrange
        var userId = "test-id";
        var token = "valid-token";

        m_UserManager.Setup(x => x.FindByIdAsync(userId))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.ConfirmEmailAsync(m_TestUser, token))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.ConfirmEmailHandler(userId, token, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.ConfirmEmailAsync(m_TestUser, token), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileHandler_WithValidRequest_UpdatesUserAndReturnsOk()
    {
        // Arrange
        var request = new UpdateProfileRequest("newUsername", "1234567890");

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
    public async Task ChangePasswordHandler_WithValidRequest_ChangesPasswordAndReturnsOk()
    {
        // Arrange
        var request = new ChangePasswordRequest("oldPass", "newPass");

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
    public async Task DeleteAccountHandler_WithValidUser_DeletesAccountAndReturnsOk()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.DeleteAsync(m_TestUser))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.DeleteAsync(m_TestUser), Times.Once);
    }

    [Fact]
    public async Task RequestEmailChangeHandler_WithValidRequest_GeneratesTokenAndReturnsOk()
    {
        // Arrange
        var request = new ChangeEmailRequest("new@example.com");

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

    [Fact]
    public async Task GetRecoveryCodesHandler_WithValidUser_ReturnsRecoveryCodes()
    {
        // Arrange
        var recoveryCodes = new[] { "code1", "code2" };
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.GenerateNewTwoFactorRecoveryCodesAsync(m_TestUser, 10))
            .ReturnsAsync(recoveryCodes);

        // Act
        var result = await AuthEndpoints.GetRecoveryCodesHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        var okResult = result.ShouldBeOfType<Ok<IEnumerable<string>>>();
        okResult.Value.ShouldBe(recoveryCodes);
    }

    [Fact]
    public async Task RegenerateRecoveryCodesHandler_WithValidUser_RegeneratesCodesAndReturnsOk()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.GenerateNewTwoFactorRecoveryCodesAsync(m_TestUser, 10))
            .ReturnsAsync(new[] { "code1", "code2" });

        // Act
        var result = await AuthEndpoints.RegenerateRecoveryCodesHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok>();
        m_UserManager.Verify(x => x.GenerateNewTwoFactorRecoveryCodesAsync(m_TestUser, 10), Times.Once);
    }
}