using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Onyx.Collector.Core;

namespace Onyx.Collector.Linux.Sources;

/// <summary>
/// Hyprland: active window over the compositor's IPC socket. Hyprland has no idle-time query, so idle is
/// approximated as "cursor and focused window unchanged" for the configured threshold.
/// </summary>
internal sealed class HyprlandSource(DesktopEntries desktopEntries, TimeProvider time) : IForegroundAppSource, IIdleTimeSource
{
    private readonly string m_Socket = FindSocket()
                                       ?? throw new InvalidOperationException("Hyprland IPC socket not found");

    private string? m_LastActivity;
    private DateTimeOffset m_LastChange = time.GetUtcNow();

    public static bool IsAvailable => Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") is not null
                                      && FindSocket() is not null;

    public ForegroundApp? GetForegroundApp()
    {
        using var doc = JsonDocument.Parse(Request("j/activewindow"));
        var root = doc.RootElement;
        if (!root.TryGetProperty("class", out var cls) || string.IsNullOrEmpty(cls.GetString()))
            return null;

        var pid = root.TryGetProperty("pid", out var p) ? p.GetInt32() : 0;
        return desktopEntries.Resolve(cls.GetString()!, ProcessInfo.GetExecutableName(pid));
    }

    public TimeSpan? GetIdleTime()
    {
        var activity = Request("j/cursorpos") + Request("activewindow").GetHashCode();
        var now = time.GetUtcNow();
        if (activity != m_LastActivity)
        {
            m_LastActivity = activity;
            m_LastChange = now;
        }

        return now - m_LastChange;
    }

    private string Request(string command)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(m_Socket));
        socket.Send(Encoding.UTF8.GetBytes(command));

        using var stream = new NetworkStream(socket);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? FindSocket()
    {
        var signature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
        if (signature is null)
            return null;

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? $"/run/user/{Environment.GetEnvironmentVariable("UID")}";
        string[] candidates =
        [
            Path.Combine(runtimeDir, "hypr", signature, ".socket.sock"),
            Path.Combine("/tmp/hypr", signature, ".socket.sock")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
