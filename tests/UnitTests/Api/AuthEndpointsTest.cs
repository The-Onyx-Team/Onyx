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
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
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
            Id = Guid.NewGuid().ToString(),
            EmailConfirmed = true,
            Email = "test@example.com",
            NormalizedEmail = "TEST@EXAMPLE.COM",
            NormalizedUserName = "TESTUSER",
            UserName = "testuser"
        };

        m_UserManager.Setup(x => x.FindByIdAsync(m_TestUser.Id))
            .ReturnsAsync(m_TestUser);

        m_ValidWebLoginModel = new AuthEndpoints.WebLoginModel
        {
            Email = "test@example.com",
            Password = "password123",
            Redirect = "/dashboard"
        };
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
        var refreshToken =
            JwtTools.GenerateRefreshToken(m_KeyAccessor.ApplicationKey, m_TestUser.Id, m_TestUser.Email!);
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

    [Fact]
    public async Task RegisterHandler_WithValidData_ReturnsOk()
    {
        // Arrange
        var request = new RegisterDto(Name: "newuser", Email: "newuser@example.com", Password: "ValidPassword123!");

        m_UserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AuthEndpoints.RegisterHandler(request, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<Ok<object>>();

        m_UserManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u =>
                u.Email == request.Email &&
                u.UserName == request.Name
            ),
            request.Password
        ), Times.Once);
    }

    [Fact]
    public async Task RegisterHandler_WithInvalidData_ReturnsBadRequestWithErrors()
    {
        // Arrange
        var request = new RegisterDto(Name: "newuser", Email: "newuser@example.com", Password: "weak");

        var identityErrors = new List<IdentityError>
        {
            new() { Code = "PasswordTooShort", Description = "Password must be at least 6 characters" }
        };
        var failedResult = IdentityResult.Failed(identityErrors.ToArray());

        m_UserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(failedResult);

        // Act
        var result = await AuthEndpoints.RegisterHandler(request, m_UserManager.Object);

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
    }

    [Fact]
    public async Task RegisterHandler_WithDuplicateEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterDto(Name: "newuser", Email: "existing@example.com", Password: "ValidPassword123!");

        var identityErrors = new List<IdentityError>
        {
            new() { Code = "DuplicateEmail", Description = "Email 'existing@example.com' is already taken." }
        };
        var failedResult = IdentityResult.Failed(identityErrors.ToArray());

        m_UserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), request.Password))
            .ReturnsAsync(failedResult);

        // Act
        var result = await AuthEndpoints.RegisterHandler(request, m_UserManager.Object);

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
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
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object);

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
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object);

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
        var result = await AuthEndpoints.DeleteAccountHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
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
        result.ShouldBeOfType<RedirectHttpResult>();
        m_UserManager.Verify(x => x.ConfirmEmailAsync(m_TestUser, token), Times.Once);
    }

    [Fact]
    public async Task ConfirmEmailHandler_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var userId = "non-existent-id";
        var token = "valid-token";

        m_UserManager.Setup(x => x.FindByIdAsync(userId))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.ConfirmEmailHandler(userId, token, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<NotFound>();
        m_UserManager.Verify(x => x.ConfirmEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmEmailHandler_WithInvalidToken_ReturnsBadRequestWithErrors()
    {
        // Arrange
        var userId = "test-id";
        var token = "invalid-token";

        var identityErrors = new List<IdentityError>
        {
            new() { Code = "InvalidToken", Description = "The confirmation token is invalid" }
        };
        var failedResult = IdentityResult.Failed(identityErrors.ToArray());

        m_UserManager.Setup(x => x.FindByIdAsync(userId))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.ConfirmEmailAsync(m_TestUser, token))
            .ReturnsAsync(failedResult);

        // Act
        var result = await AuthEndpoints.ConfirmEmailHandler(userId, token, m_UserManager.Object);

        // Assert
        var badRequestResult = result.ShouldBeOfType<BadRequest<IEnumerable<IdentityError>>>();
        badRequestResult.Value.ShouldBe(identityErrors);
    }

    [Fact]
    public async Task RegenerateRecoveryCodesHandler_WithValidUser_ReturnsOkWithCodes()
    {
        // Arrange
        var expectedCodes = new[] { "code1", "code2", "code3" };

        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(m_TestUser);
        m_UserManager.Setup(x => x.GenerateNewTwoFactorRecoveryCodesAsync(m_TestUser, 10))
            .ReturnsAsync(expectedCodes);

        // Act
        var result = await AuthEndpoints.RegenerateRecoveryCodesHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        var okResult = result.ShouldBeOfType<Ok<IEnumerable<string>>>();
        okResult.Value.ShouldBe(expectedCodes);
        m_UserManager.Verify(x => x.GenerateNewTwoFactorRecoveryCodesAsync(m_TestUser, 10), Times.Once);
    }

    [Fact]
    public async Task RegenerateRecoveryCodesHandler_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        m_UserManager.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser)null!);

        // Act
        var result = await AuthEndpoints.RegenerateRecoveryCodesHandler(m_HttpContext.Object, m_UserManager.Object);

        // Assert
        result.ShouldBeOfType<NotFound>();
        m_UserManager.Verify(
            x => x.GenerateNewTwoFactorRecoveryCodesAsync(It.IsAny<ApplicationUser>(), It.IsAny<int>()), Times.Never);
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
        result.ShouldBeOfType<RedirectHttpResult>();
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
        result.ShouldBeOfType<RedirectHttpResult>();
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

#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
        var authService =
            Mock.Get(
                m_HttpContext.Object.RequestServices.GetService(
                    typeof(IAuthenticationService)) as IAuthenticationService);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.

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