using Microsoft.Win32;
using Onyx.Collector.Core.Browser;

namespace Onyx.App.WinUI;

/// <summary>
/// Registers <c>onyx-native-host.exe</c> (shipped next to the app) as the native messaging host of the Onyx
/// browser extension, per user: one manifest file per browser family, referenced from the registry keys the
/// browsers read. Unregistering removes both, so the extension can no longer reach Onyx.
/// </summary>
internal static class NativeHostRegistration
{
    public const string HostExecutable = "onyx-native-host.exe";

    /// <summary>Registry roots of browsers that read Chrome-style manifests (allowed_origins).</summary>
    private static readonly string[] s_ChromiumKeys =
    [
        @"Software\Google\Chrome\NativeMessagingHosts",
        @"Software\Chromium\NativeMessagingHosts",
        @"Software\Microsoft\Edge\NativeMessagingHosts",
        @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts",
        @"Software\Vivaldi\NativeMessagingHosts"
    ];

    private const string FirefoxKey = @"Software\Mozilla\NativeMessagingHosts";

    private static string ManifestDirectory => Path.Combine(FileSystem.AppDataDirectory, "native-host");

    public static string HostPath => Path.Combine(AppContext.BaseDirectory, HostExecutable);

    /// <exception cref="InvalidOperationException">The host executable is missing from the installation.</exception>
    public static void Register()
    {
        if (!File.Exists(HostPath))
            throw new InvalidOperationException(
                $"{HostExecutable} is missing from {AppContext.BaseDirectory}. Reinstall Onyx, or for a development " +
                $"build publish src/Onyx.Collector/Onyx.Collector.NativeHost into that folder.");

        Directory.CreateDirectory(ManifestDirectory);
        var chrome = Path.Combine(ManifestDirectory, "chrome.json");
        var firefox = Path.Combine(ManifestDirectory, "firefox.json");
        File.WriteAllText(chrome, BrowserBridge.ChromeHostManifest(HostPath));
        File.WriteAllText(firefox, BrowserBridge.FirefoxHostManifest(HostPath));

        foreach (var root in s_ChromiumKeys)
            SetDefault(root, chrome);
        SetDefault(FirefoxKey, firefox);
    }

    public static void Unregister()
    {
        foreach (var root in s_ChromiumKeys.Append(FirefoxKey))
            Registry.CurrentUser.DeleteSubKeyTree($@"{root}\{BrowserBridge.NativeHostName}", throwOnMissingSubKey: false);

        if (Directory.Exists(ManifestDirectory))
            Directory.Delete(ManifestDirectory, recursive: true);
    }

    private static void SetDefault(string root, string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{root}\{BrowserBridge.NativeHostName}");
        key.SetValue("", manifestPath);
    }
}
