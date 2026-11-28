using Onyx.App.Shared.Services;

namespace Onyx.App.Services;

public class MauiPlatformService : IPlatformService
{
    public bool IsMaui() => true;

    public string Description =>
        $"{(DeviceInfo.Current.Platform == DevicePlatform.WinUI ? "Windows" : DeviceInfo.Current.Platform.ToString())} " +
        $"{DeviceInfo.Current.VersionString} ({DeviceInfo.Current.Model}), app {AppInfo.Current.VersionString} " +
        $"build {AppInfo.Current.BuildString}";

    public async Task<bool> RequestNotificationsAsync()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;
        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        return status == PermissionStatus.Granted;
#else
        await Task.CompletedTask;
        return true;
#endif
    }

#if ANDROID
    public bool? StayInControlAllowed => Control.StayInControlService.IsEnabled();

    public void OpenStayInControlSettings() => Control.StayInControlService.OpenSettings();

    public Task SettingsSavedAsync(Onyx.Collector.Core.Crypto.VaultSettings settings)
    {
        UsageData.AndroidReminders.SaveSettings(settings);
        return Task.CompletedTask;
    }
#endif

    public string? ReminderDelivery =>
#if ANDROID
        "This phone checks every 15 minutes (Android doesn't allow more often), so focus mode nudges can come a little late.";
#elif WINDOWS
        "This computer checks every 30 seconds while Onyx runs (also in the notification area).";
#else
        null;
#endif
}
