namespace Onyx.App.Services.Collection;

/// <summary>How this installation names itself when it registers with the server.</summary>
public static class DeviceIdentity
{
    public static string Name => string.IsNullOrWhiteSpace(DeviceInfo.Current.Name)
        ? DeviceInfo.Current.Model
        : DeviceInfo.Current.Name;

    public static string Platform =>
        DeviceInfo.Current.Platform == DevicePlatform.WinUI ? "Windows" : DeviceInfo.Current.Platform.ToString();
}
