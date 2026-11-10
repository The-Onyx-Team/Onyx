using Onyx.Collector.Core.Browser;

namespace Onyx.Collector.Linux;

/// <summary>
/// Registers the native messaging host with the browsers installed for this user, so the Onyx browser extension
/// can reach the agent. Browsers start the host by path without arguments of our choosing, so the registered path
/// is a small script that runs <c>onyx-agent native-host</c>.
/// </summary>
internal static class NativeHostRegistration
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Config => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config");

    /// <summary>Profile directory of each Chromium browser; the host manifest goes into its NativeMessagingHosts.</summary>
    private static readonly string[] s_ChromiumBrowsers =
    [
        "google-chrome", "google-chrome-beta", "google-chrome-unstable", "chromium", "BraveSoftware/Brave-Browser",
        "microsoft-edge", "microsoft-edge-beta", "microsoft-edge-dev", "vivaldi"
    ];

    /// <summary>Firefox and forks read manifests from these directories.</summary>
    private static readonly (string BrowserDirectory, string HostDirectory)[] s_FirefoxBrowsers =
    [
        (".mozilla", ".mozilla/native-messaging-hosts"),
        (".librewolf", ".librewolf/native-messaging-hosts"),
        (".waterfox", ".waterfox/native-messaging-hosts")
    ];

    public static string LauncherPath => Path.Combine(AgentFiles.DataDirectory, "native-host");
    private static string ManifestName => BrowserBridge.NativeHostName + ".json";

    /// <summary>Writes the launcher and a manifest for every installed browser.</summary>
    /// <returns>Directories the manifest was written to.</returns>
    public static List<string> Install(string agentPath)
    {
        Directory.CreateDirectory(AgentFiles.DataDirectory);
        File.WriteAllText(LauncherPath, $"#!/bin/sh\nexec '{agentPath.Replace("'", "'\\''")}' native-host \"$@\"\n");
        File.SetUnixFileMode(LauncherPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var written = new List<string>();
        foreach (var dir in ChromiumHostDirectories().Where(d => Directory.Exists(Path.GetDirectoryName(d))))
        {
            Write(dir, BrowserBridge.ChromeHostManifest(LauncherPath));
            written.Add(dir);
        }

        foreach (var (browser, hosts) in s_FirefoxBrowsers)
        {
            if (!Directory.Exists(Path.Combine(Home, browser)))
                continue;
            var dir = Path.Combine(Home, hosts);
            Write(dir, BrowserBridge.FirefoxHostManifest(LauncherPath));
            if (!written.Contains(dir))
                written.Add(dir);
        }

        return written;
    }

    /// <summary>Removes every manifest and the launcher, so the extension can no longer reach the agent.</summary>
    public static void Uninstall()
    {
        foreach (var dir in ChromiumHostDirectories().Concat(s_FirefoxBrowsers.Select(f => Path.Combine(Home, f.HostDirectory))))
            File.Delete(Path.Combine(dir, ManifestName));
        File.Delete(LauncherPath);
    }

    /// <summary>Directories that currently contain the manifest.</summary>
    public static List<string> Installed() =>
        ChromiumHostDirectories()
            .Concat(s_FirefoxBrowsers.Select(f => Path.Combine(Home, f.HostDirectory)))
            .Distinct()
            .Where(d => File.Exists(Path.Combine(d, ManifestName)))
            .ToList();

    private static IEnumerable<string> ChromiumHostDirectories() =>
        s_ChromiumBrowsers.Select(b => Path.Combine(Config, b, "NativeMessagingHosts"));

    private static void Write(string dir, string manifest)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ManifestName), manifest);
    }
}
