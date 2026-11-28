using System.Reflection;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;
using Onyx.Collector.Core;
using Onyx.App.UsageData;

namespace Onyx.Collector.Wear;

#if ONYX_PRIVATE_NETWORK
// Self-hosted server behind a VPN/LAN: http:// and user-installed CAs (Resources/xml/network_security_config.xml).
[Application(UsesCleartextTraffic = true, NetworkSecurityConfig = "@xml/network_security_config")]
#elif DEBUG
// The local dev server in the emulator (http://10.0.2.2:5262).
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class WearApplication(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : Application(handle, ownership)
{
    public override void OnCreate()
    {
        base.OnCreate();
        WearSyncWorker.Schedule(this);
    }
}

/// <summary>The watch's settings and the shared API client.</summary>
public static class WearServices
{
    private const string Preferences = "onyx";
    private const string ServerKey = "server";

    private static OnyxApiClient? s_Client;
    private static string? s_ClientServer;

    private static ISharedPreferences Prefs => Application.Context.GetSharedPreferences(Preferences, FileCreationMode.Private)!;

    /// <summary>Set with -p:OnyxServerUrl when building; otherwise the dev server (Debug) or the public one.</summary>
    public static string DefaultServer =>
        typeof(WearServices).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "OnyxServerUrl")?.Value
#if DEBUG
        ?? "http://10.0.2.2:5262";
#else
        ?? "https://onyx.g-martin.work";
#endif

    public static string Server
    {
        get => Prefs.GetString(ServerKey, null) ?? DefaultServer;
        set => Prefs.Edit()!.PutString(ServerKey, value.Trim().TrimEnd('/'))!.Apply();
    }

    public static ISharedPreferences State => Prefs;

    /// <summary>The API client for the current server (recreated when it changes).</summary>
    public static OnyxApiClient Client
    {
        get
        {
            var server = Server;
            if (s_Client is null || s_ClientServer != server)
            {
                var http = new HttpClient { BaseAddress = new Uri(server + "/"), Timeout = TimeSpan.FromSeconds(30) };
                s_Client = new OnyxApiClient(http, new WearCredentialStore());
                s_ClientServer = server;
            }

            return s_Client;
        }
    }

    public static AndroidUsageReader Reader { get; } = new();

    public static string DeviceName => string.IsNullOrWhiteSpace(Build.Model) ? "Watch" : Build.Model!;

    public const string Platform = "Wear OS";
}

/// <summary>Tokens and the encryption key in the app's private storage, which only this app can read.</summary>
internal sealed class WearCredentialStore : ICredentialStore
{
    private static string FilePath => Path.Combine(Application.Context.FilesDir!.AbsolutePath, "credentials.json");

    public Task<CollectorCredentials?> LoadAsync()
    {
        try
        {
            return Task.FromResult(File.Exists(FilePath)
                ? JsonSerializer.Deserialize<CollectorCredentials>(File.ReadAllText(FilePath))
                : null);
        }
        catch (JsonException)
        {
            return Task.FromResult<CollectorCredentials?>(null);
        }
    }

    public Task SaveAsync(CollectorCredentials credentials)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(credentials));
        File.Move(temp, FilePath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        File.Delete(FilePath);
        return Task.CompletedTask;
    }
}
