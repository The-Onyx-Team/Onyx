using System.Reflection;
using Microsoft.Extensions.Logging;
using Onyx.Collector.Core;
using Tmds.DBus.Protocol;

namespace Onyx.Collector.Linux.Sources;

/// <summary>
/// KDE Plasma on Wayland: the agent takes the name <c>io.github.theonyxteam.OnyxAgent</c> on the session bus and
/// loads a small KWin script (packaging/linux/kwin/onyx-focus.js) that calls it whenever another window becomes
/// active. Nothing has to be installed by hand.
/// </summary>
internal sealed class KWinSource : IForegroundAppSource, IMethodHandler, IDisposable
{
    public const string BusName = "io.github.theonyxteam.OnyxAgent";
    private const string ObjectPath = "/io/github/theonyxteam/OnyxAgent";
    private const string PluginName = "onyx-focus";

    private readonly SessionBus m_Bus;
    private readonly DesktopEntries m_Entries;
    private readonly ILogger m_Logger;
    private readonly Lock m_Lock = new();
    private (string Class, string DesktopFile, uint Pid)? m_Active;
    private bool m_Reported;

    private KWinSource(SessionBus bus, DesktopEntries entries, ILogger logger)
    {
        m_Bus = bus;
        m_Entries = entries;
        m_Logger = logger;
    }

    public static bool IsKde =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Registers on the bus and loads the KWin script; null if KWin can't be reached.</summary>
    public static KWinSource? TryStart(SessionBus bus, DesktopEntries entries, ILogger logger)
    {
        var source = new KWinSource(bus, entries, logger);
        try
        {
            bus.Connection.AddMethodHandler(source);
            if (!bus.RequestName(BusName))
            {
                logger.LogError("Another onyx-agent already owns {Name} on the session bus", BusName);
                return null;
            }

            source.LoadScript();
            return source;
        }
        catch (Exception e) when (e is DBusException or TimeoutException or AggregateException or IOException)
        {
            logger.LogError("Could not load the KWin script: {Reason}", e.Message);
            return null;
        }
    }

    public ForegroundApp? GetForegroundApp()
    {
        (string Class, string DesktopFile, uint Pid)? active;
        lock (m_Lock)
            active = m_Active;
        if (active is not { } window)
            return null;

        var key = !string.IsNullOrEmpty(window.Class) ? window.Class : window.DesktopFile;
        return string.IsNullOrEmpty(key)
            ? null
            : m_Entries.Resolve(key, ProcessInfo.GetExecutableName((int)window.Pid), (int)window.Pid);
    }

    private void LoadScript()
    {
        // The script goes next to the agent's state; KWin reads it from there.
        var folder = System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d
            ? d
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "onyx");
        Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, "kwin-onyx-focus.js");
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("kwin/onyx-focus.js")
                              ?? throw new IOException("The KWin script is missing from the agent"))
        using (var target = File.Create(file))
            resource.CopyTo(target);

        // A script from an earlier run (or an agent restart) would report twice.
        try
        {
            m_Bus.Call("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript",
                m => m.GetBodyReader().ReadBool(), "s", PluginName);
        }
        catch (DBusException)
        {
        }

        var id = m_Bus.Call("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "loadScript",
            m => m.GetBodyReader().ReadInt32(), "ss", file, PluginName);
        if (id < 0)
            throw new IOException("KWin refused the script");

        // Plasma 6 puts the script at /Scripting/Script<id>, Plasma 5 at /<id>.
        foreach (var path in new[] { $"/Scripting/Script{id}", $"/{id}" })
        {
            try
            {
                m_Bus.Call<object?>("org.kde.KWin", path, "org.kde.kwin.Script", "run", _ => null);
                m_Logger.LogInformation("KWin script loaded ({Path})", path);
                return;
            }
            catch (DBusException)
            {
            }
        }

        throw new IOException("KWin did not start the script");
    }

    // D-Bus: Focus(s resourceClass, s desktopFileName, u pid, s caption), called by the KWin script.
    public string Path => ObjectPath;

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (request.InterfaceAsString == BusName && request.MemberAsString == "Focus")
        {
            var reader = request.GetBodyReader();
            var resourceClass = reader.ReadString();
            var desktopFile = reader.ReadString();
            // KWin sends numbers from JavaScript as int or uint depending on the version.
            var pid = request.SignatureAsString?.Length >= 3 && request.SignatureAsString[2] == 'i'
                ? (uint)Math.Max(0, reader.ReadInt32())
                : reader.ReadUInt32();
            lock (m_Lock)
                m_Active = string.IsNullOrEmpty(resourceClass) && string.IsNullOrEmpty(desktopFile)
                    ? null
                    : (resourceClass, desktopFile, pid);
            if (!m_Reported)
                m_Logger.LogInformation("KWin reports the active window");
            m_Reported = true;

            if (!context.NoReplyExpected)
            {
                using var writer = context.CreateReplyWriter(null!);
                context.Reply(writer.CreateMessage());
            }
        }
        else if (!context.NoReplyExpected)
        {
            context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unknown method");
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            m_Bus.Call("org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript",
                m => m.GetBodyReader().ReadBool(), "s", PluginName);
        }
        catch (Exception)
        {
            // KWin may be gone already (logout).
        }
    }
}
