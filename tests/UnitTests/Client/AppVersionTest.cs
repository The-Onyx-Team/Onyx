using JetBrains.Annotations;
using Onyx.App.Shared.Services;
using Shouldly;
using Xunit;

namespace UnitTests.Client;

[TestSubject(typeof(AppVersion))]
public class AppVersionTest
{
    [Theory]
    [InlineData("0.0+3f2a1b9c8d7e6f5a", "0.0", "3f2a1b9")]
    [InlineData("0.0 (3f2a1b9)", "0.0", "3f2a1b9")]
    [InlineData("1.2.0", "1.2.0", null)]
    public void Parse_ReadsNumberAndCommit(string text, string number, string? commit) =>
        AppVersion.Parse(text).ShouldBe(new AppVersion(number, commit));

    [Fact]
    public void Builds_CarryTheVersionFromDirectoryBuildProps()
    {
        AppVersion.Current.Number.ShouldBe("0.0");
        AppVersion.Of(typeof(Onyx.App.Web.Services.ServerInfo).Assembly).Number.ShouldBe("0.0");
    }
}
