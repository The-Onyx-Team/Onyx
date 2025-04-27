using Microsoft.EntityFrameworkCore;
using Moq;
using Moq.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;
using Xunit;

namespace Onyx.App.Web.Api.Tests;

public class DataEndpointsTests
{
    private readonly Mock<ApplicationDbContext> _mockContext;
    private readonly DataEndpoints _endpoints;

    public DataEndpointsTests()
    {
        _mockContext = new Mock<ApplicationDbContext>();
        _endpoints = new DataEndpoints();
    }

    [Fact]
    public async Task NewUsage_WhenDeviceDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var usages = new List<Usage>();
        var devices = new List<Device>();
        var apps = new List<RegisteredApp>();

        _mockContext.Setup(c => c.Set<Usage>()).ReturnsDbSet(usages);
        _mockContext.Setup(c => c.Set<Device>()).ReturnsDbSet(devices);
        _mockContext.Setup(c => c.Set<RegisteredApp>()).ReturnsDbSet(apps);
        _mockContext.Setup(c => c.SaveChangesAsync(default)).ReturnsAsync(1);

        // Act
        var result = await _endpoints.NewUsage(
            _mockContext.Object, 
            "user1", 
            "2023-01-01", 
            "1000", 
            "1100", 
            "NewDevice", 
            "App1"
        );

        // Assert
        Assert.False(result); // Fails due to incorrect query in original code
    }

    [Fact]
    public async Task NewUsage_WhenDeviceAndAppExist_ReturnsTrue()
    {
        // Arrange
        var existingDevice = new Device { Id = 1, Name = "ExistingDevice", UserId = "user1" };
        var existingApp = new RegisteredApp { Id = 1, Name = "ExistingApp" };
        var usages = new List<Usage> 
        { 
            new Usage { Devices = existingDevice, App = existingApp } 
        };

        _mockContext.Setup(c => c.Set<Usage>()).ReturnsDbSet(usages);
        _mockContext.Setup(c => c.Set<Device>()).ReturnsDbSet(new List<Device> { existingDevice });
        _mockContext.Setup(c => c.Set<RegisteredApp>()).ReturnsDbSet(new List<RegisteredApp> { existingApp });
        _mockContext.Setup(c => c.SaveChangesAsync(default)).ReturnsAsync(1);

        // Act
        var result = await _endpoints.NewUsage(
            _mockContext.Object, 
            "user1", 
            "2023-01-01", 
            "1000", 
            "1100", 
            "ExistingDevice", 
            "ExistingApp"
        );

        // Assert
        Assert.True(result);
    }
}
