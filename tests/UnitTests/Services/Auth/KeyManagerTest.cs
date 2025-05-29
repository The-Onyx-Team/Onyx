namespace UnitTests.Services.Auth;

[TestSubject(typeof(KeyManager))]
public class KeyManagerTest
{
    private const string TestPath = "test_key.pem";

    [Fact]
    public async Task CreateKeyIfNotExists_ShouldCreateNewKey_WhenFileDoesNotExist()
    {
        // Arrange
        if (File.Exists(TestPath))
            File.Delete(TestPath);

        // Act
        await KeyManager.CreateKeyIfNotExists(TestPath);

        // Assert
        File.Exists(TestPath).ShouldBeTrue();
        var keyBytes = await File.ReadAllBytesAsync(TestPath);
        keyBytes.Length.ShouldBeGreaterThan(0);

        // Cleanup
        File.Delete(TestPath);
    }

    [Fact]
    public async Task CreateKeyIfNotExists_ShouldNotCreateNewKey_WhenFileExists()
    {
        // Arrange
        var initialKey = RSA.Create();
        var initialKeyBytes = initialKey.ExportRSAPrivateKey();
        await File.WriteAllBytesAsync(TestPath, initialKeyBytes);

        // Act
        await KeyManager.CreateKeyIfNotExists(TestPath);

        // Assert
        var finalKeyBytes = await File.ReadAllBytesAsync(TestPath);
        finalKeyBytes.ShouldBe(initialKeyBytes);

        // Cleanup
        File.Delete(TestPath);
    }

    [Fact]
    public async Task LoadKey_ShouldLoadValidRsaKey()
    {
        // Arrange
        var originalKey = RSA.Create();
        var keyBytes = originalKey.ExportRSAPrivateKey();
        await File.WriteAllBytesAsync(TestPath, keyBytes);

        // Act
        var loadedKey = await KeyManager.LoadKey(TestPath);

        // Assert
        loadedKey.ShouldNotBeNull();
        var loadedKeyBytes = loadedKey.ExportRSAPrivateKey();
        loadedKeyBytes.ShouldBe(keyBytes);

        // Cleanup
        File.Delete(TestPath);
    }
}