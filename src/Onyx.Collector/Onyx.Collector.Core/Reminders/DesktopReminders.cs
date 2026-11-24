using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onyx.Collector.Core.Crypto;

namespace Onyx.Collector.Core.Reminders;

/// <summary>Shows a notification on the device (toast, libnotify, ...).</summary>
public interface INudgeNotifier
{
    void Show(Nudge nudge);
}

/// <summary>
/// Reminders and focus mode for the desktop collectors (Windows, Linux): checks the <see cref="ActivityLog"/>
/// against the user's settings every half minute and shows what is due. The settings are read again every few
/// minutes, so changes made on another device arrive soon.
/// </summary>
public sealed class DesktopReminders(
    OnyxApiClient api,
    ICredentialStore credentials,
    ActivityLog activity,
    INudgeNotifier notifier,
    TimeProvider time,
    ILogger? logger = null)
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SettingsInterval = TimeSpan.FromMinutes(3);

    private readonly ILogger m_Logger = logger ?? NullLogger.Instance;
    private readonly ReminderState m_State = new();
    private VaultSettings? m_Settings;
    private DateTimeOffset m_SettingsRead = DateTimeOffset.MinValue;

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await CheckAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One check. Public so tests and hosts with their own loop can drive it.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        if (m_Settings is null || now - m_SettingsRead >= SettingsInterval)
        {
            try
            {
                m_Settings = await api.GetVaultSettingsAsync(ct) ?? m_Settings;
                m_SettingsRead = now;
            }
            catch (Exception e) when (e is HttpRequestException or NotSignedInException or TaskCanceledException)
            {
                // Offline or locked: keep using the last settings.
                m_SettingsRead = now;
                m_Logger.LogDebug(e, "Could not read the settings for reminders");
            }
        }

        if (m_Settings is not { } settings || (settings.Reminders.Count == 0 && settings.Focus.Until is null && settings.Focus.Schedules.Count == 0))
            return;

        var deviceId = (await credentials.LoadAsync())?.DeviceId;
        foreach (var nudge in ReminderEngine.Evaluate(settings, deviceId, activity.Snapshot(), now,
                     time.LocalTimeZone, m_State))
        {
            try
            {
                notifier.Show(nudge);
            }
            catch (Exception e)
            {
                m_Logger.LogWarning(e, "Could not show a notification");
            }
        }
    }
}
