using System.Security.Claims;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Onyx.App.Web.Api;
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
}