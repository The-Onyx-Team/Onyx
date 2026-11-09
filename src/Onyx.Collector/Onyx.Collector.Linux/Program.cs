using Microsoft.Extensions.Logging;
using Onyx.Collector.Core;
using Onyx.Collector.Linux;
using Onyx.Collector.Linux.Sources;

const string usage = """
    Usage: onyx-agent <command>

      login [--server URL] [--email EMAIL] [--device-name NAME]
                     Sign in and save the server address. The password is read from the
                     terminal, or from ONYX_PASSWORD when stdin is not a terminal.
      run            Collect usage and upload it (what the systemd service runs).
      status         Show sign-in state, detected desktop session and pending data.
      logout         Forget the tokens. Pending data is kept.
    """;

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    })
    .SetMinimumLevel(Environment.GetEnvironmentVariable("ONYX_DEBUG") is null ? LogLevel.Information : LogLevel.Debug));
var logger = loggerFactory.CreateLogger("onyx-agent");

return args.FirstOrDefault() switch
{
    "login" => await LoginAsync(args[1..]),
    "run" => await RunAsync(),
    "status" => await StatusAsync(),
    "logout" => await LogoutAsync(),
    _ => Usage()
};

int Usage()
{
    Console.Error.WriteLine(usage);
    return 64;
}

OnyxApiClient CreateClient(AgentConfig config) =>
    new(new HttpClient { BaseAddress = new Uri(config.ServerUrl), Timeout = TimeSpan.FromSeconds(60) },
        new FileCredentialStore(), loggerFactory.CreateLogger<OnyxApiClient>());

async Task<int> LoginAsync(string[] options)
{
    string? Option(string name)
    {
        var i = Array.IndexOf(options, name);
        return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
    }

    var existing = AgentFiles.LoadConfig();
    var server = Option("--server") ?? existing?.ServerUrl ?? Prompt("Server URL", "https://onyx.g-martin.work");
    var email = Option("--email") ?? Prompt("Email", null);
    var password = Console.IsInputRedirected
        ? Environment.GetEnvironmentVariable("ONYX_PASSWORD") ?? Console.ReadLine() ?? ""
        : ReadPassword();

    var config = new AgentConfig(server.TrimEnd('/'), Option("--device-name") ?? existing?.DeviceName);
    try
    {
        await CreateClient(config).LoginAsync(email, password);
    }
    catch (Exception e) when (e is NotSignedInException or HttpRequestException)
    {
        Console.Error.WriteLine($"Login failed: {e.Message}");
        return 1;
    }

    AgentFiles.SaveConfig(config);
    Console.WriteLine($"Signed in to {config.ServerUrl}. This device will appear as \"{config.EffectiveDeviceName}\".");
    Console.WriteLine("Start collecting with: systemctl --user enable --now onyx-agent");
    return 0;
}

async Task<int> RunAsync()
{
    if (AgentFiles.LoadConfig() is not { } config)
    {
        logger.LogError("Not configured. Run 'onyx-agent login' first");
        return 78;
    }

    var (source, idle, description) = DetectSession();
    if (source is null)
    {
        logger.LogError("Unsupported desktop session: {Description}", description);
        return 69;
    }

    logger.LogInformation("Collecting from {Session}, uploading to {Server}", description, config.ServerUrl);

    var time = TimeProvider.System;
    var store = new PendingUsageStore(AgentFiles.PendingPath, loggerFactory.CreateLogger<PendingUsageStore>());
    var tracker = new ForegroundTracker(source, idle, store, time, logger: loggerFactory.CreateLogger<ForegroundTracker>());
    var sync = new UsageSyncService(CreateClient(config), store, source as IAppIconSource ?? new IconSource(),
        time, config.EffectiveDeviceName, "Linux", loggerFactory.CreateLogger<UsageSyncService>());

    using var stop = new CancellationTokenSource();
    using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
        System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            stop.Cancel();
        });
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };

    await Task.WhenAll(
        tracker.RunAsync(stop.Token),
        sync.RunAsync(TimeSpan.FromMinutes(5), stop.Token));

    // Last attempt so a clean shutdown (logout, reboot) loses nothing.
    using var final = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await sync.SyncOnceAsync(final.Token);
    return 0;
}

async Task<int> StatusAsync()
{
    var config = AgentFiles.LoadConfig();
    var signedIn = config is not null && await new FileCredentialStore().LoadAsync() is not null;
    var (source, _, description) = DetectSession();

    Console.WriteLine($"Server:    {config?.ServerUrl ?? "(not configured)"}");
    Console.WriteLine($"Device:    {config?.EffectiveDeviceName ?? Environment.MachineName}");
    Console.WriteLine($"Signed in: {(signedIn ? "yes" : "no")}");
    Console.WriteLine($"Session:   {description}{(source is null ? " (unsupported)" : "")}");
    Console.WriteLine($"Pending:   {new PendingUsageStore(AgentFiles.PendingPath).Count} hourly buckets");

    if (source is not null)
    {
        try
        {
            var app = source.GetForegroundApp();
            Console.WriteLine($"Focused:   {(app is null ? "(none)" : $"{app.Name} [{app.Identifier}]")}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Focused:   error: {e.Message}");
        }
    }

    return signedIn ? 0 : 1;
}

async Task<int> LogoutAsync()
{
    await new FileCredentialStore().ClearAsync();
    Console.WriteLine("Signed out.");
    return 0;
}

(IForegroundAppSource? Source, IIdleTimeSource? Idle, string Description) DetectSession()
{
    var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "unknown desktop";
    var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
    var entries = new DesktopEntries();

    if (HyprlandSource.IsAvailable)
    {
        var hyprland = new HyprlandSource(entries, TimeProvider.System);
        return (new IconAwareSource(hyprland, entries), new SessionIdleSource(hyprland), "Hyprland (Wayland)");
    }

    if (SwaySource.IsAvailable)
        return (new IconAwareSource(new SwaySource(entries), entries), new SessionIdleSource(), "Sway (Wayland)");

    if (X11Source.IsAvailable)
    {
        var x11 = new X11Source(entries);
        return (new IconAwareSource(x11, entries), new SessionIdleSource(x11), $"{desktop} (X11)");
    }

    // GNOME and KDE on Wayland don't let ordinary clients see the focused window; that needs a Shell
    // extension (GNOME) or a KWin script (KDE).
    return (null, null, $"{desktop} ({sessionType}): only Hyprland, Sway and X11 sessions are supported so far");
}

static string Prompt(string label, string? fallback)
{
    Console.Write(fallback is null ? $"{label}: " : $"{label} [{fallback}]: ");
    var value = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(value) ? fallback ?? "" : value;
}

static string ReadPassword()
{
    Console.Write("Password: ");
    var password = new System.Text.StringBuilder();
    while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            password.Length--;
        else if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }

    Console.WriteLine();
    return password.ToString();
}

/// <summary>Adds icon lookup from .desktop files to a foreground source.</summary>
internal sealed class IconAwareSource(IForegroundAppSource inner, DesktopEntries entries) : IForegroundAppSource, IAppIconSource
{
    public ForegroundApp? GetForegroundApp() => inner.GetForegroundApp();
    public byte[]? GetIconPng(string appIdentifier) => entries.GetIconPng(appIdentifier);
}

internal sealed class IconSource : IAppIconSource
{
    public byte[]? GetIconPng(string appIdentifier) => null;
}
