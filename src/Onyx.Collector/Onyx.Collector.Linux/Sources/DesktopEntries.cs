using Onyx.Collector.Core;

namespace Onyx.Collector.Linux.Sources;

/// <summary>
/// Maps window classes / app ids to the display name and icon of the matching <c>.desktop</c> file.
/// </summary>
internal sealed class DesktopEntries
{
    private sealed record Entry(string Name, string? Icon, string? Category);

    private static readonly string[] s_IconSizes = ["256x256", "128x128", "96x96", "64x64", "48x48"];

    private readonly Dictionary<string, Entry> m_ByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> m_IconByIdentifier = new();

    public DesktopEntries()
    {
        foreach (var dir in ApplicationDirectories())
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.AllDirectories))
                TryAdd(file);
        }
    }

    /// <summary>Identifier, name and category for a window.</summary>
    /// <param name="windowClass">Wayland app id or X11 WM_CLASS.</param>
    /// <param name="executable">Executable name of the window's process, if known.</param>
    /// <param name="processId">The window's process, 0 if unknown.</param>
    public ForegroundApp Resolve(string windowClass, string? executable, int processId = 0)
    {
        var identifier = "linux:" + windowClass.ToLowerInvariant();

        var entry = Lookup(windowClass) ?? (executable is null ? null : Lookup(executable));
        if (entry?.Icon is { } icon)
            m_IconByIdentifier.TryAdd(identifier, icon);

        return new ForegroundApp(identifier, entry?.Name ?? windowClass, entry?.Category, processId);
    }

    /// <summary>PNG bytes of the app's icon from the icon themes, if one was found.</summary>
    public byte[]? GetIconPng(string identifier)
    {
        if (!m_IconByIdentifier.TryGetValue(identifier, out var icon))
            return null;

        if (Path.IsPathRooted(icon))
            return icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(icon) ? File.ReadAllBytes(icon) : null;

        foreach (var dataDir in DataDirectories())
        {
            foreach (var size in s_IconSizes)
            {
                var path = Path.Combine(dataDir, "icons", "hicolor", size, "apps", icon + ".png");
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
            }

            var pixmap = Path.Combine(dataDir, "pixmaps", icon + ".png");
            if (File.Exists(pixmap))
                return File.ReadAllBytes(pixmap);
        }

        return null;
    }

    private Entry? Lookup(string key)
    {
        if (m_ByKey.TryGetValue(key, out var entry))
            return entry;

        // Reverse-DNS app ids ("org.gnome.Nautilus") often match the file name, plain classes the last part.
        var lastPart = key.Split('.').Last();
        return m_ByKey.GetValueOrDefault(lastPart);
    }

    private void TryAdd(string file)
    {
        string? name = null, icon = null, wmClass = null, exec = null, categories = null;
        var inMainSection = false;
        var hidden = false;

        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith('['))
                {
                    inMainSection = line == "[Desktop Entry]";
                    continue;
                }

                if (!inMainSection)
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var value = line[(eq + 1)..].Trim();
                switch (line[..eq].Trim())
                {
                    case "Name": name ??= value; break;
                    case "Icon": icon = value; break;
                    case "StartupWMClass": wmClass = value; break;
                    case "Exec": exec = value; break;
                    case "Categories": categories = value; break;
                    case "NoDisplay" or "Hidden" when value == "true": hidden = true; break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (name is null)
            return;

        var entry = new Entry(name, icon, MainCategory(categories));
        var fileKey = Path.GetFileNameWithoutExtension(file);

        // Visible entries win over hidden helpers that share a key.
        void Add(string? key)
        {
            if (string.IsNullOrEmpty(key) || (hidden && m_ByKey.ContainsKey(key)))
                return;
            m_ByKey[key] = entry;
        }

        Add(wmClass);
        Add(fileKey);
        Add(fileKey.Split('.').Last());
        if (exec is not null)
            Add(Path.GetFileName(exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(p => !p.Contains('=')) ?? ""));
    }

    /// <summary>First freedesktop main category, e.g. "Development" from "Development;IDE;".</summary>
    private static string? MainCategory(string? categories)
    {
        string[] main = ["AudioVideo", "Audio", "Video", "Development", "Education", "Game", "Graphics", "Network",
            "Office", "Science", "Settings", "System", "Utility"];
        return categories?.Split(';').FirstOrDefault(c => main.Contains(c));
    }

    private static IEnumerable<string> DataDirectories()
    {
        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var dirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share";
        return new[] { home }.Concat(dirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
            .Append("/var/lib/flatpak/exports/share")
            .Distinct();
    }

    // Later directories are read first so that user entries (XDG_DATA_HOME) override system ones.
    private static IEnumerable<string> ApplicationDirectories() =>
        DataDirectories().Reverse().Select(d => Path.Combine(d, "applications"));
}
