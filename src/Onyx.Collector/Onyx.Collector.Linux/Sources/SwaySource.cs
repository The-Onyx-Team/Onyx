using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Onyx.Collector.Core;

namespace Onyx.Collector.Linux.Sources;

/// <summary>Sway (and other i3-IPC compositors): focused window from the layout tree.</summary>
internal sealed class SwaySource(DesktopEntries desktopEntries) : IForegroundAppSource
{
    private const int GetTree = 4;
    private static readonly byte[] s_Magic = "i3-ipc"u8.ToArray();

    private readonly string m_Socket = Environment.GetEnvironmentVariable("SWAYSOCK")
                                       ?? throw new InvalidOperationException("SWAYSOCK is not set");

    public static bool IsAvailable => Environment.GetEnvironmentVariable("SWAYSOCK") is { } s && File.Exists(s);

    public ForegroundApp? GetForegroundApp()
    {
        using var doc = JsonDocument.Parse(Request(GetTree));
        if (FindFocused(doc.RootElement) is not { } node)
            return null;

        var pid = node.TryGetProperty("pid", out var p) ? p.GetInt32() : 0;
        var appId = node.TryGetProperty("app_id", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (string.IsNullOrEmpty(appId)
            && node.TryGetProperty("window_properties", out var props)
            && props.TryGetProperty("class", out var cls))
            appId = cls.GetString();

        var exe = ProcessInfo.GetExecutableName(pid);
        var key = string.IsNullOrEmpty(appId) ? exe : appId;
        return key is null ? null : desktopEntries.Resolve(key, exe);
    }

    private static JsonElement? FindFocused(JsonElement node)
    {
        if (node.TryGetProperty("focused", out var focused) && focused.GetBoolean()
                                                            && node.TryGetProperty("pid", out _))
            return node;

        foreach (var key in new[] { "nodes", "floating_nodes" })
        {
            if (!node.TryGetProperty(key, out var children))
                continue;
            foreach (var child in children.EnumerateArray())
                if (FindFocused(child) is { } found)
                    return found;
        }

        return null;
    }

    private string Request(int type)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(m_Socket));

        var header = new byte[14];
        s_Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6), 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), type);
        socket.Send(header);

        ReceiveExactly(socket, header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(6));
        var payload = new byte[length];
        ReceiveExactly(socket, payload);
        return Encoding.UTF8.GetString(payload);
    }

    private static void ReceiveExactly(Socket socket, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = socket.Receive(buffer, read, buffer.Length - read, SocketFlags.None);
            if (n == 0)
                throw new IOException("Sway IPC connection closed");
            read += n;
        }
    }
}
