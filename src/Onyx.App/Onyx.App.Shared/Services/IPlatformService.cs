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
}
