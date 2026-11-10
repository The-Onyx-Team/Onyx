using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Onyx.Collector.Core.Browser;

/// <summary>One state report, as the native messaging host forwards it to the collector (one JSON object per line).</summary>
public sealed record BrowserMessage(
    [property: JsonPropertyName("browserPid")] int BrowserProcessId,
    [property: JsonPropertyName("focused")] bool Focused,
    [property: JsonPropertyName("domain")] string? Domain);

/// <summary>What the extension sends to the native messaging host.</summary>
internal sealed record ExtensionMessage(
    [property: JsonPropertyName("focused")] bool Focused,
    [property: JsonPropertyName("domain")] string? Domain);

/// <summary>Source-generated serialization, so the native host can be compiled ahead of time.</summary>
[JsonSerializable(typeof(BrowserMessage))]
[JsonSerializable(typeof(ExtensionMessage))]
internal sealed partial class BrowserJson : JsonSerializerContext;

/// <summary>
/// Local endpoint the native messaging hosts connect to. A named pipe on Windows and a Unix domain socket on
/// Linux (that is how .NET implements named pipes there); both are restricted to the current user.
/// </summary>
public static class BrowserBridge
{
    /// <summary>Per user, since on Linux the socket lives in the shared temp directory.</summary>
    public static string PipeName => $"onyx-browser-{Environment.UserName}";

    /// <summary>Name of the native messaging host the extension connects to (same in every browser).</summary>
    public const string NativeHostName = "com.onyx_team.onyx.browser";

    /// <summary>Firefox add-on id of the extension.</summary>
    public const string FirefoxExtensionId = "websites@onyx-team.github.io";

    /// <summary>Chrome/Edge extension ids allowed to talk to the host: the id of the unpacked development build
    /// (fixed by the key in its manifest). Add the store ids here once the extension is published.</summary>
    public static readonly string[] ChromeExtensionIds = ["nkgaflfkpndlfchgfnjmjdkjdfpihnei"];


    /// <summary>Native messaging host manifest for Chromium-based browsers.</summary>
    public static string ChromeHostManifest(string hostPath) => JsonSerializer.Serialize(new
    {
        name = NativeHostName,
        description = "Onyx website recording",
        path = hostPath,
        type = "stdio",
        allowed_origins = ChromeExtensionIds.Select(id => $"chrome-extension://{id}/").ToArray()
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Native messaging host manifest for Firefox.</summary>
    public static string FirefoxHostManifest(string hostPath) => JsonSerializer.Serialize(new
    {
        name = NativeHostName,
        description = "Onyx website recording",
        path = hostPath,
        type = "stdio",
        allowed_extensions = new[] { FirefoxExtensionId }
    }, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Collector side of the bridge: accepts host connections and feeds <see cref="BrowserActivity"/>.</summary>
public sealed class BrowserBridgeServer(BrowserActivity activity, ILogger? logger = null)
{
    private readonly ILogger m_Logger = logger ?? NullLogger.Instance;
    private long m_NextConnection;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(BrowserBridge.PipeName, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException e)
            {
                m_Logger.LogWarning("Browser bridge unavailable ({Reason}); retrying in a minute", e.Message);
                await Delay(TimeSpan.FromMinutes(1), cancellationToken);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }
            catch (IOException e)
            {
                await server.DisposeAsync();
                m_Logger.LogDebug(e, "Browser bridge connection failed");
                continue;
            }

            _ = HandleAsync(server, Interlocked.Increment(ref m_NextConnection), cancellationToken);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, long connection, CancellationToken ct)
    {
        m_Logger.LogInformation("Browser extension connected");
        try
        {
            using var reader = new StreamReader(pipe);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                try
                {
                    if (JsonSerializer.Deserialize(line, BrowserJson.Default.BrowserMessage) is { } message)
                    {
                        activity.Update(connection, message.BrowserProcessId, message.Focused, message.Domain);
                        m_Logger.LogDebug("Browser {Pid}: focused={Focused} domain={Domain}", message.BrowserProcessId,
                            message.Focused, message.Domain);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed lines; the next report replaces the state anyway.
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            activity.Disconnected(connection);
            await pipe.DisposeAsync();
            m_Logger.LogInformation("Browser extension disconnected");
        }
    }

    private static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
