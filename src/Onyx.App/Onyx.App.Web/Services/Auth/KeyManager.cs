using System.Security.Cryptography;

namespace Onyx.App.Web.Services.Auth;

public class KeyManager
{
    public static async Task CreateKeyIfNotExists(string path)
    {
        if (File.Exists(path))
            return;
        
        var rsaKey = RSA.Create();
        var privateKey = rsaKey.ExportRSAPrivateKey();
        await File.WriteAllBytesAsync(path, privateKey);

        // Private signing key: readable by the app's user only.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    
    public static async Task<RSA> LoadKey(string path)
    {
        var content = await File.ReadAllBytesAsync(path);
        
        var rsaKey = RSA.Create();
        rsaKey.ImportRSAPrivateKey(content, out _);
        return rsaKey;
    }
}