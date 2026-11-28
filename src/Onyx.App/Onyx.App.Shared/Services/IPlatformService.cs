namespace Onyx.App.Shared.Services;

public interface IPlatformService
{
    bool IsMaui();

    /// <summary>Where this UI runs, for the About page, e.g. "Android 15 (Pixel 9)" or "Web browser".</summary>
    string Description => IsMaui() ? "App" : "Web browser";

    /// <summary>Asks for permission to show notifications (reminders, focus mode) where the OS needs it.</summary>
    /// <returns>Whether this device can show them.</returns>
    Task<bool> RequestNotificationsAsync() => Task.FromResult(IsMaui());

    /// <summary>How reminders arrive on this device, shown on the focus page; null in the browser.</summary>
    string? ReminderDelivery => null;

    /// <summary>Stay in control mode on this device: null where it isn't available (browser, desktop), otherwise
    /// whether the system lets Onyx see which app opens (Android: its accessibility service is on).</summary>
    bool? StayInControlAllowed => null;

    /// <summary>Opens the system screen where Stay in control is allowed (Android: accessibility settings).</summary>
    void OpenStayInControlSettings()
    {
    }

    /// <summary>Called after the user saved settings on this device, so background parts (reminders, Stay in
    /// control) use them at once instead of at their next check.</summary>
    Task SettingsSavedAsync(Onyx.Collector.Core.Crypto.VaultSettings settings) => Task.CompletedTask;
}
