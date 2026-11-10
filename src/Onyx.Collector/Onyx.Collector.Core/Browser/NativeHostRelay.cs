using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Onyx.Collector.Core.Browser;

/// <summary>
/// The native messaging host: the browser starts it, writes the extension's messages to its stdin, and this relays
/// them to the running collector over <see cref="BrowserBridge"/>. If the collector is not running, messages are
/// dropped; the extension repeats its state every minute, so it reconnects once the collector is back.
/// </summary>
/// <remarks>Stdout belongs to the protocol: the browser parses it as messages and disconnects on anything
/// malformed. The only message sent back is <c>{"collector": true|false}</c>, for the extension's popup.</remarks>
public sealed class NativeHostRelay(int browserProcessId, Func<CancellationToken, Task<Stream>>? connect = null)
    : IAsyncDisposable
{
    /// <summary>The browser limits messages to the host to 4 GB; the extension's are tiny.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    private readonly Func<CancellationToken, Task<Stream>> m_Connect = connect ?? ConnectPipeAsync;
    private Stream? m_Collector;
    private bool? m_ReportedConnected;

    /// <summary>Relays messages until the browser closes stdin.</summary>
    /// <param name="output">Stdout, to tell the extension whether the collector is reachable.</param>
    public async Task RunAsync(Stream input, Stream? output = null, CancellationToken cancellationToken = default)
    {
        while (await ReadMessageAsync(input, cancellationToken) is { } json)
        {
            ExtensionMessage? message;
            try
            {
                message = JsonSerializer.Deserialize(json, BrowserJson.Default.ExtensionMessage);
            }
            catch (JsonException)
            {
                continue;
            }

            if (message is null)
                continue;

            var connected = await ForwardAsync(new BrowserMessage(browserProcessId, message.Focused, message.Domain),
                cancellationToken);
            if (output is not null && connected != m_ReportedConnected)
            {
                m_ReportedConnected = connected;
                await WriteMessageAsync(output, connected ? """{"collector":true}""" : """{"collector":false}""",
                    cancellationToken);
            }
        }
    }

    /// <summary>Reads one native messaging frame: a 32-bit length in native (little-endian) byte order, then UTF-8 JSON.</summary>
    /// <returns>Null at the end of the stream.</returns>
    public static async Task<string?> ReadMessageAsync(Stream input, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(input, header, ct))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxMessageBytes)
            throw new InvalidDataException($"Native message of {length} bytes");

        var payload = new byte[length];
        if (!await ReadExactlyOrEndAsync(input, payload, ct))
            return null;
        return Encoding.UTF8.GetString(payload);
    }

    /// <summary>Writes one native messaging frame (used by tests and for the protocol's other direction).</summary>
    public static async Task WriteMessageAsync(Stream output, string json, CancellationToken ct = default)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await output.WriteAsync(header, ct);
        await output.WriteAsync(payload, ct);
        await output.FlushAsync(ct);
    }

    /// <returns>Whether the collector received it.</returns>
    private async Task<bool> ForwardAsync(BrowserMessage message, CancellationToken ct)
    {
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, BrowserJson.Default.BrowserMessage) + "\n");

        // One retry with a fresh connection: the collector may have restarted since the last message.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                m_Collector ??= await m_Connect(ct);
                await m_Collector.WriteAsync(line, ct);
                await m_Collector.FlushAsync(ct);
                return true;
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException
                                          or ObjectDisposedException)
            {
                if (m_Collector is not null)
                    await m_Collector.DisposeAsync();
                m_Collector = null;
            }
        }

        return false;
    }

    private static async Task<Stream> ConnectPipeAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", BrowserBridge.PipeName, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(TimeSpan.FromMilliseconds(500), ct);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream input, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await input.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
                return false;
            read += n;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (m_Collector is not null)
            await m_Collector.DisposeAsync();
    }
}
