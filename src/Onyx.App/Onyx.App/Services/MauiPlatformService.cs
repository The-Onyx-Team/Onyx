using Onyx.App.Shared.Services;

namespace Onyx.App.Services;

public class MauiPlatformService : IPlatformService
{
    public bool IsMaui() => true;

    public string Description =>
        $"{(DeviceInfo.Current.Platform == DevicePlatform.WinUI ? "Windows" : DeviceInfo.Current.Platform.ToString())} " +
        $"{DeviceInfo.Current.VersionString} ({DeviceInfo.Current.Model}), app {AppInfo.Current.VersionString} " +
        $"build {AppInfo.Current.BuildString}";
}
