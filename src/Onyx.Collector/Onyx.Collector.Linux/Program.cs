using Microsoft.Extensions.Logging;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Browser;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Onyx.Collector.Linux;
using Onyx.Collector.Linux.Sources;
using Onyx.Collector.Core.Reminders;

const string usage = """
    Usage: onyx-agent <command>

      login [--server URL] [--email EMAIL] [--device-name NAME] [--code CODE]
                     Sign in and save the server address. The password is read from the
                     terminal, or from ONYX_PASSWORD when stdin is not a terminal. With
                     two-factor authentication it asks for the code (or ONYX_2FA_CODE).
                     Then asks for your encryption passphrase (see unlock).
      unlock [--recovery-key]
                     Enter your encryption passphrase (or ONYX_PASSPHRASE) so the agent can
                     encrypt what it uploads. Data is end-to-end encrypted; the key is kept
                     in ~/.config/onyx/credentials.json (mode 600).
      run            Collect usage and upload it (what the systemd service runs).
      status         Show sign-in state, detected desktop session and pending data.
      logout         Forget the tokens. Pending data is kept.
      version        Show the agent's version.
      gnome install|remove
                     GNOME on Wayland: install the small GNOME Shell extension that tells the
                     agent which app is in front (log out and in once afterwards). KDE Plasma
                     needs nothing: the agent loads a KWin script itself.
      websites on|off
                     Record which website the browser shows (domain only). Needs the Onyx browser
                     extension; 'on' registers the agent with the installed browsers. Restart the
                     service afterwards: systemctl --user restart onyx-agent
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
    "unlock" => await UnlockAsync(args[1..]),
    "run" => await RunAsync(),
    "status" => await StatusAsync(),
    "logout" => await LogoutAsync(),
    "version" or "--version" => PrintVersion(),
    "websites" => Websites(args[1..]),
    "gnome" => Gnome(args[1..]),
    // Started by the browser through the launcher script NativeHostRegistration writes.
    "native-host" => await NativeHostAsync(),
    _ => Usage()
};

int PrintVersion()
{
    Console.WriteLine($"onyx-agent {AgentVersion()}");
    return 0;
}

static string AgentVersion()
{
    var text = System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AgentConfig).Assembly)
        ?.InformationalVersion ?? "unknown";
    var plus = text.IndexOf('+');
    return plus < 0 ? text : $"{text[..plus]} ({text[(plus + 1)..Math.Min(text.Length, plus + 8)]})";
}

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
        var client = CreateClient(config);
        try
        {
            await client.LoginAsync(email, password, twoFactorCode: Option("--code"));
        }
        catch (TwoFactorRequiredException)
        {
            // Two-factor authentication: ask for the code from the authenticator app (or a recovery code).
            var code = Console.IsInputRedirected
                ? Environment.GetEnvironmentVariable("ONYX_2FA_CODE") ?? Console.ReadLine()
                : Prompt("Code from your authenticator app (or a recovery code)", null);
            if (string.IsNullOrWhiteSpace(code))
                throw;
            await client.LoginAsync(email, password, twoFactorCode: code.Trim());
        }
    }
    catch (Exception e) when (e is NotSignedInException or HttpRequestException)
    {
        Console.Error.WriteLine($"Login failed: {e.Message}");
        return 1;
    }

    AgentFiles.SaveConfig(config);
    Console.WriteLine($"Signed in to {config.ServerUrl}. This device will appear as \"{config.EffectiveDeviceName}\".");
    var unlocked = await UnlockAsync([]);
    if (unlocked == 0)
        Console.WriteLine("Start collecting with: systemctl --user enable --now onyx-agent");
    return unlocked;
}

async Task<int> UnlockAsync(string[] options)
{
    if (AgentFiles.LoadConfig() is not { } config)
    {
        Console.Error.WriteLine("Not configured. Run 'onyx-agent login' first.");
        return 78;
    }

    var client = CreateClient(config);
    VaultDto? vault;
    try
    {
        vault = await client.GetVaultInfoAsync();
    }
    catch (Exception e) when (e is NotSignedInException or HttpRequestException)
    {
        Console.Error.WriteLine($"Could not reach the server: {e.Message}");
        return 1;
    }

    if (vault is null)
    {
        Console.Error.WriteLine("Your account has no encryption set up yet. Sign in once on the web dashboard or in the");
        Console.Error.WriteLine("Onyx app, which asks you to choose a passphrase, then run: onyx-agent unlock");
        return 2;
    }

    var useRecoveryKey = options.Contains("--recovery-key");
    var secret = Console.IsInputRedirected
        ? Environment.GetEnvironmentVariable(useRecoveryKey ? "ONYX_RECOVERY_KEY" : "ONYX_PASSPHRASE") ?? Console.ReadLine() ?? ""
        : ReadSecret(useRecoveryKey ? "Recovery key" : "Encryption passphrase");

    try
    {
        var unlocked = useRecoveryKey
            ? await UserVault.UnlockWithRecoveryKeyAsync(DotNetCrypto.Instance, vault, secret)
            : await UserVault.UnlockAsync(DotNetCrypto.Instance, vault, secret);
        await client.StoreVaultKeyAsync(unlocked.Keys.MasterKey);
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
        Console.Error.WriteLine(useRecoveryKey ? "That is not the recovery key of this account." : "Wrong passphrase.");
        return 1;
    }

    Console.WriteLine("Unlocked: uploads are encrypted on this computer before they are sent.");
    return 0;
}

async Task<int> RunAsync()
{
    if (AgentFiles.LoadConfig() is not { } config)
    {
        logger.LogError("Not configured. Run 'onyx-agent login' first");
        return 78;
    }

    var (source, idle, description) = DetectSession(start: true);
    if (source is null)
    {
        logger.LogError("Unsupported desktop session: {Description}", description);
        return 69;
    }

    logger.LogInformation("Collecting from {Session}, uploading to {Server}", description, config.ServerUrl);

    var time = TimeProvider.System;
    var store = new PendingUsageStore(AgentFiles.PendingPath, loggerFactory.CreateLogger<PendingUsageStore>());

    // Website recording: the browser extension reports through the native messaging host to this bridge.
    BrowserActivity? browser = null;
    if (config.RecordWebsites)
    {
        browser = new BrowserActivity(time);
        // Refresh the registration: browsers installed since, or a new agent path after an update.
        var registered = NativeHostRegistration.Install(Environment.ProcessPath!);
        logger.LogInformation("Recording websites; host registered for {Count} browser(s)", registered.Count);
    }

    // Reminders and focus mode check the recent activity on this computer and notify through the desktop.
    var activity = new ActivityLog();
    var tracker = new ForegroundTracker(source, idle, store, time, logger: loggerFactory.CreateLogger<ForegroundTracker>(),
        websiteSource: browser, activity: activity);
    var client = CreateClient(config);
    var sync = new UsageSyncService(client, store, source as IAppIconSource ?? new IconSource(),
        time, config.EffectiveDeviceName, "Linux", loggerFactory.CreateLogger<UsageSyncService>(), browser);
    var reminders = new DesktopReminders(client, new FileCredentialStore(), activity,
        new LinuxNotifier(loggerFactory.CreateLogger<LinuxNotifier>()), time, loggerFactory.CreateLogger<DesktopReminders>());

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
        sync.RunAsync(TimeSpan.FromMinutes(5), stop.Token),
        reminders.RunAsync(stop.Token),
        browser is null
            ? Task.CompletedTask
            : new BrowserBridgeServer(browser, loggerFactory.CreateLogger<BrowserBridgeServer>()).RunAsync(stop.Token));

    (source as IDisposable)?.Dispose();

    // Last attempt so a clean shutdown (logout, reboot) loses nothing.
    using var final = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await sync.SyncOnceAsync(final.Token);
    return 0;
}

async Task<int> StatusAsync()
{
    var config = AgentFiles.LoadConfig();
    var signedIn = config is not null && await new FileCredentialStore().LoadAsync() is not null;
    var (source, _, description) = DetectSession(start: false);

    Console.WriteLine($"Version:   {AgentVersion()}");
    Console.WriteLine($"Server:    {config?.ServerUrl ?? "(not configured)"}");
    Console.WriteLine($"Device:    {config?.EffectiveDeviceName ?? Environment.MachineName}");
    Console.WriteLine($"Signed in: {(signedIn ? "yes" : "no")}");
    var credentials = await new FileCredentialStore().LoadAsync();
    Console.WriteLine($"Encrypted: {(credentials?.VaultKey is null ? "no key yet, run 'onyx-agent unlock'" : "yes, key on this computer")}");
    Console.WriteLine($"Session:   {description}{(source is null ? " (unsupported)" : "")}");
    Console.WriteLine($"Pending:   {new PendingUsageStore(AgentFiles.PendingPath).Count} hourly buckets");
    var hosts = NativeHostRegistration.Installed();
    Console.WriteLine($"Websites:  {(config?.RecordWebsites == true ? "on" : "off")}" +
                      (hosts.Count > 0 ? $" (registered in {string.Join(", ", hosts)})" : ""));

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

int Websites(string[] options)
{
    if (AgentFiles.LoadConfig() is not { } config)
    {
        Console.Error.WriteLine("Not configured. Run 'onyx-agent login' first.");
        return 78;
    }

    switch (options.FirstOrDefault())
    {
        case "on":
            var dirs = NativeHostRegistration.Install(Environment.ProcessPath!);
            AgentFiles.SaveConfig(config with { RecordWebsites = true });
            Console.WriteLine("Website recording is on. Only the domain of the active tab is recorded.");
            Console.WriteLine(dirs.Count == 0
                ? "No supported browser profile found yet; run this again after starting your browser once."
                : $"Registered for: {string.Join(", ", dirs)}");
            Console.WriteLine("Next: install the Onyx Websites browser extension, then restart the agent:");
            Console.WriteLine("  systemctl --user restart onyx-agent");
            return 0;
        case "off":
            NativeHostRegistration.Uninstall();
            AgentFiles.SaveConfig(config with { RecordWebsites = false });
            Console.WriteLine("Website recording is off. Restart the agent: systemctl --user restart onyx-agent");
            Console.WriteLine("Websites recorded so far can be deleted in the dashboard under Settings.");
            return 0;
        default:
            return Usage();
    }
}

async Task<int> NativeHostAsync()
{
    // The browser is our parent: the launcher script replaces itself with this process (exec).
    var browserPid = ProcessInfo.GetParentProcessId(Environment.ProcessId);
    await using var relay = new NativeHostRelay(browserPid);
    await relay.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
    return 0;
}

async Task<int> LogoutAsync()
{
    await new FileCredentialStore().ClearAsync();
    Console.WriteLine("Signed out.");
    return 0;
}

// start: for "run"; the KWin script is only loaded then (status just describes the session).
(IForegroundAppSource? Source, IIdleTimeSource? Idle, string Description) DetectSession(bool start)
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

    // GNOME and KDE on Wayland don't let ordinary programs see the focused window: GNOME needs the Onyx Shell
    // extension, KDE gets a KWin script. On X11 the X11 source below works for them too.
    // The systemd user service may not get XDG_SESSION_TYPE, but GNOME and KDE pass WAYLAND_DISPLAY on.
    var wayland = sessionType == "wayland" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    if (wayland && GnomeShellSource.IsGnome)
    {
        var bus = SessionBus.TryConnect();
        var gnome = bus is null ? null : new GnomeShellSource(bus, entries);
        if (gnome?.IsAvailable() == true)
            return (new IconAwareSource(gnome, entries), new SessionIdleSource(), "GNOME (Wayland, Onyx extension)");
        return (null, null, "GNOME (Wayland): the Onyx GNOME extension is not active. Run 'onyx-agent gnome install', " +
                            "then log out and in again");
    }

    if (wayland && KWinSource.IsKde)
    {
        if (!start)
            return (new NoFocusSource(), null, "KDE Plasma (Wayland, KWin script)");
        if (SessionBus.TryConnect() is { } bus
            && KWinSource.TryStart(bus, entries, loggerFactory.CreateLogger<KWinSource>()) is { } kwin)
            return (new IconAwareSource(kwin, entries), new SessionIdleSource(), "KDE Plasma (Wayland, KWin script)");
        return (null, null, "KDE Plasma (Wayland): the KWin script could not be loaded (see the log)");
    }

    if (X11Source.IsAvailable)
    {
        var x11 = new X11Source(entries);
        return (new IconAwareSource(x11, entries), new SessionIdleSource(x11), $"{desktop} (X11)");
    }

    return (null, null, $"{desktop} ({sessionType}): only Hyprland, Sway, GNOME, KDE Plasma and X11 sessions are supported");
}

int Gnome(string[] options)
{
    switch (options.FirstOrDefault())
    {
        case "install":
            var folder = GnomeShellSource.Install();
            Console.WriteLine($"Installed the Onyx GNOME extension in {folder}.");
            var enabled = RunTool("gnome-extensions", "enable", GnomeShellSource.ExtensionUuid);
            Console.WriteLine(enabled
                ? "Enabled it. GNOME on Wayland only loads new extensions after you log out and in again; then restart the agent:"
                : $"Enable it after logging out and in again: gnome-extensions enable {GnomeShellSource.ExtensionUuid}");
            Console.WriteLine("  systemctl --user restart onyx-agent");
            return 0;
        case "remove":
            RunTool("gnome-extensions", "disable", GnomeShellSource.ExtensionUuid);
            GnomeShellSource.Uninstall();
            Console.WriteLine("Removed the Onyx GNOME extension.");
            return 0;
        default:
            return Usage();
    }
}

static bool RunTool(string file, params string[] arguments)
{
    try
    {
        var start = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return false;
    }
}

static string Prompt(string label, string? fallback)
{
    Console.Write(fallback is null ? $"{label}: " : $"{label} [{fallback}]: ");
    var value = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(value) ? fallback ?? "" : value;
}

static string ReadPassword() => ReadSecret("Password");

static string ReadSecret(string label)
{
    Console.Write($"{label}: ");
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
internal sealed class IconAwareSource(IForegroundAppSource inner, DesktopEntries entries) : IForegroundAppSource, IAppIconSource, IDisposable
{
    public ForegroundApp? GetForegroundApp() => inner.GetForegroundApp();
    public byte[]? GetIconPng(string appIdentifier) => entries.GetIconPng(appIdentifier);

    /// <summary>Unloads what the source set up (the KWin script on KDE).</summary>
    public void Dispose() => (inner as IDisposable)?.Dispose();
}

/// <summary>For "status" on KDE: the focused window is only known once "run" has loaded the KWin script.</summary>
internal sealed class NoFocusSource : IForegroundAppSource
{
    public ForegroundApp? GetForegroundApp() => null;
}

internal sealed class IconSource : IAppIconSource
{
    public byte[]? GetIconPng(string appIdentifier) => null;
}
