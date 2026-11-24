using System.Reflection;
using Onyx.Collector.Core;
using Tmds.DBus.Protocol;

namespace Onyx.Collector.Linux.Sources;

/// <summary>
/// GNOME on Wayland: the focused window from the Onyx GNOME Shell extension (packaging/linux/gnome-extension),
/// which answers <c>io.github.theonyxteam.Onyx.GetFocus</c> on the session bus. Install it with
/// <c>onyx-agent gnome install</c>.
/// </summary>
internal sealed class GnomeShellSource(SessionBus bus, DesktopEntries entries) : IForegroundAppSource
{
    public const string ExtensionUuid = "onyx@the-onyx-team.github.io";
    private const string Path = "/io/github/theonyxteam/Onyx";
    private const string Interface = "io.github.theonyxteam.Onyx";

    public static bool IsGnome =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the extension answers (installed, enabled, and GNOME Shell restarted since).</summary>
    public bool IsAvailable()
    {
        try
        {
            Query();
            return true;
        }
        catch (Exception e) when (e is DBusException or TimeoutException or AggregateException)
        {
            return false;
        }
    }

    public ForegroundApp? GetForegroundApp()
    {
        var (wmClass, appId, pid) = Query();
        // On Wayland Mutter's WM class is the app id the app sets (as on Sway and Hyprland); GNOME's own app id
        // ("org.gnome.Nautilus.desktop") is the fallback.
        var key = !string.IsNullOrEmpty(wmClass) ? wmClass
            : appId.EndsWith(".desktop", StringComparison.Ordinal) ? appId[..^".desktop".Length] : appId;
        if (string.IsNullOrEmpty(key))
            return null;
        return entries.Resolve(key, ProcessInfo.GetExecutableName((int)pid), (int)pid);
    }

    private (string WmClass, string AppId, uint Pid) Query() =>
        bus.Call("org.gnome.Shell", Path, Interface, "GetFocus", m =>
        {
            var reader = m.GetBodyReader();
            var wmClass = reader.ReadString();
            var appId = reader.ReadString();
            var pid = reader.ReadUInt32();
            return (wmClass, appId, pid);
        });

    /// <summary>Copies the extension into the user's GNOME Shell extensions and enables it.</summary>
    /// <returns>The folder it was installed to.</returns>
    public static string Install()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d
            ? d
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var folder = System.IO.Path.Combine(dataHome, "gnome-shell", "extensions", ExtensionUuid);
        Directory.CreateDirectory(folder);
        foreach (var file in new[] { "metadata.json", "extension.js" })
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream($"gnome-extension/{file}")
                                 ?? throw new InvalidOperationException($"{file} is missing from the agent");
            using var target = File.Create(System.IO.Path.Combine(folder, file));
            resource.CopyTo(target);
        }

        return folder;
    }

    public static void Uninstall()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d
            ? d
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var folder = System.IO.Path.Combine(dataHome, "gnome-shell", "extensions", ExtensionUuid);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}
