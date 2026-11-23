using System.Net;
using JetBrains.Annotations;
using Onyx.App.Web.Api;
using Shouldly;
using Xunit;

namespace UnitTests.Api;

[TestSubject(typeof(IconProxyEndpoints))]
public class IconProxyTest
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.8.0.1")]
    [InlineData("172.20.1.1")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.1")]
    public void PrivateAddresses_AreBlocked(string address) =>
        IconProxyEndpoints.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700::6810:84e5")]
    public void PublicAddresses_AreAllowed(string address) =>
        IconProxyEndpoints.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();

    [Fact]
    public async Task OnlyHttpAddresses_AreFetched()
    {
        (await IconProxyEndpoints.FetchAsync("file:///etc/passwd", CancellationToken.None))
            .ShouldBeOfType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>();
        (await IconProxyEndpoints.FetchAsync("http://127.0.0.1/icon.png", CancellationToken.None))
            .ShouldBeOfType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>()
            .Value.ShouldBe("Only public internet addresses can be fetched.");
    }
}
