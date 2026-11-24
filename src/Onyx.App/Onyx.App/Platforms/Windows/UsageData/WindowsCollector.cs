using Microsoft.Extensions.Logging;
using Onyx.App.Services.Collection;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Browser;
using Onyx.Collector.Core.Reminders;
using Onyx.App.WinUI;

namespace Onyx.App.UsageData;

/// <summary>
/// Desktop collection on Windows: samples the foreground app every few seconds into a persisted queue and
/// uploads it every five minutes. Runs for as long as the app runs (also while hidden in the tray).
/// With website recording on, the Onyx browser extension reports the active tab's domain through
/// <c>onyx-native-host.exe</c>, and browser time is split by website.
/// </summary>
public sealed class WindowsCollector : IStatsService, IDisposable
{
    private static readonly TimeSpan s_SyncInterval = TimeSpan.FromMinutes(5);
    private const string RecordWebsitesKey = "onyx_record_websites";

    private readonly PendingUsageStore m_Store;
    private readonly ForegroundTracker m_Tracker;
    private readonly UsageSyncService m_Sync;
    private readonly BrowserActivity m_Browser = new(TimeProvider.System);
    private readonly BrowserBridgeServer m_Bridge;
    private readonly ActivityLog m_Activity = new();
    private readonly DesktopReminders m_Reminders;
    private readonly ILogger m_Logger;
    private volatile bool m_RecordWebsites = Preferences.Default.Get(RecordWebsitesKey, false);
    private readonly CancellationTokenSource m_Stop = new();
    private Task? m_Running;

    public WindowsCollector(OnyxApiClient client, ICredentialStore credentials, ILoggerFactory loggerFactory)
    {
        var sources = new WindowsSources();
        m_Logger = loggerFactory.CreateLogger<WindowsCollector>();
        m_Bridge = new BrowserBridgeServer(m_Browser, loggerFactory.CreateLogger<BrowserBridgeServer>());
        m_Store = new PendingUsageStore(Path.Combine(FileSystem.AppDataDirectory, "pending-usage.json"),
            loggerFactory.CreateLogger<PendingUsageStore>());
        m_Tracker = new ForegroundTracker(sources, sources, m_Store, TimeProvider.System,
            logger: loggerFactory.CreateLogger<ForegroundTracker>(),
            websiteSource: new SwitchableWebsiteSource(this), activity: m_Activity);
        m_Reminders = new DesktopReminders(client, credentials, m_Activity, new TrayNotifier(m_Logger), TimeProvider.System,
            loggerFactory.CreateLogger<DesktopReminders>());
        m_Sync = new UsageSyncService(client, m_Store, sources, TimeProvider.System, DeviceIdentity.Name,
            DeviceIdentity.Platform, loggerFactory.CreateLogger<UsageSyncService>(), m_Browser);
    }

    public void Start()
    {
        if (m_Running is not null)
            return;

        if (m_RecordWebsites)
        {
            // Refresh the registration: the app may have moved or been updated since it was turned on.
            try
            {
                NativeHostRegistration.Register();
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                m_Logger.LogWarning("Could not register the browser host: {Reason}", e.Message);
            }
        }

        m_Running = Task.WhenAll(
            Task.Run(() => m_Tracker.RunAsync(m_Stop.Token)),
            Task.Run(() => m_Sync.RunAsync(s_SyncInterval, m_Stop.Token)),
            Task.Run(() => m_Bridge.RunAsync(m_Stop.Token)),
            Task.Run(() => m_Reminders.RunAsync(m_Stop.Token)));
    }

    /// <summary>Stops sampling and makes a last upload attempt; called when the app quits.</summary>
    public async Task StopAsync()
    {
        await m_Stop.CancelAsync();
        if (m_Running is not null)
            await m_Running;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await m_Sync.SyncOnceAsync(timeout.Token);
    }

    public CollectionStatus GetStatus() =>
        new(Supported: true, HasPermission: true, m_Sync.LastSuccess, m_Sync.LastError, m_Store.Count,
            RecordWebsites: m_RecordWebsites,
            ActiveWebsite: m_RecordWebsites ? m_Browser.LastReport?.Domain : null);

    public Task<CollectionStatus> SetRecordWebsitesAsync(bool enabled)
    {
        if (enabled)
            NativeHostRegistration.Register();
        else
            NativeHostRegistration.Unregister();

        m_RecordWebsites = enabled;
        Preferences.Default.Set(RecordWebsitesKey, enabled);
        return Task.FromResult(GetStatus());
    }

    /// <summary>Reports websites only while recording is on; the bridge itself keeps running.</summary>
    private sealed class SwitchableWebsiteSource(WindowsCollector owner) : IWebsiteSource
    {
        public string? GetDomain(ForegroundApp app) => owner.m_RecordWebsites ? owner.m_Browser.GetDomain(app) : null;
    }

    /// <summary>Reminders and focus mode as notifications from the tray icon.</summary>
    private sealed class TrayNotifier(ILogger logger) : INudgeNotifier
    {
        public void Show(Nudge nudge)
        {
            if (!TrayIcon.Notify(nudge.Title, nudge.Message))
                logger.LogInformation("{Title}: {Message}", nudge.Title, nudge.Message);
        }
    }

    public void OpenUsageAccessSettings()
    {
    }

    public async Task<CollectionStatus> SyncNowAsync()
    {
        await m_Sync.SyncOnceAsync(CancellationToken.None);
        return GetStatus();
    }

    public void Dispose() => m_Stop.Dispose();
}
