using JetBrains.Annotations;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.DataBaseSchema.Identity;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Auth;

[TestSubject(typeof(Avatar))]
public class AvatarTest
{
    private static readonly byte[] s_Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
    private static readonly byte[] s_Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0];
    private static readonly byte[] s_Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    [Fact]
    public void Validate_AcceptsPngJpegAndWebp()
    {
        Avatar.Validate(s_Png, "image/png").ShouldBeNull();
        Avatar.Validate(s_Jpeg, "image/jpeg").ShouldBeNull();
        Avatar.Validate(s_Webp, "image/webp").ShouldBeNull();
    }

    [Fact]
    public void Validate_RejectsWrongTypesContentAndSize()
    {
        Avatar.Validate([], "image/png").ShouldNotBeNull();
        Avatar.Validate(s_Png, "image/gif").ShouldNotBeNull();
        Avatar.Validate(s_Jpeg, "image/png").ShouldNotBeNull();       // content does not match the type
        Avatar.Validate("RIFF\0\0\0\0WAVE"u8.ToArray(), "image/webp").ShouldNotBeNull();
        Avatar.Validate([.. s_Png, .. new byte[ApplicationUser.MaxAvatarBytes]], "image/png").ShouldNotBeNull();
    }
}

[TestSubject(typeof(ReturnUrl))]
public class ReturnUrlTest
{
    [Theory]
    [InlineData(null, "/home")]
    [InlineData("", "/home")]
    [InlineData("/devices", "/devices")]
    [InlineData("/settings?tab=apps#apps", "/settings?tab=apps#apps")]
    [InlineData("/", "/home")]
    [InlineData("/account/login?returnUrl=/x", "/home")]
    [InlineData("https://evil.example/", "/home")]
    [InlineData("//evil.example/", "/home")]
    [InlineData("/\\evil.example", "/home")]
    [InlineData("javascript:alert(1)", "/home")]
    public void Sanitize_AllowsOnlyLocalPaths(string? input, string expected) =>
        ReturnUrl.Sanitize(input).ShouldBe(expected);
}
