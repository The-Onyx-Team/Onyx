using Microsoft.Win32;

namespace Onyx.App.WinUI;

/// <summary>
/// Starts Onyx hidden in the tray when the user signs in to Windows, so usage is collected without opening the
/// app. Uses the per-user Run key (no admin rights); users can still turn it off in Task Manager > Startup apps.
/// </summary>
internal static class Autostart
{
    public const string MinimizedArgument = "--minimized";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Onyx";

    public static void Register()
    {
        if (Environment.ProcessPath is not { } exe)
            return;

        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var command = $"\"{exe}\" {MinimizedArgument}";
        if (key.GetValue(ValueName) as string != command)
            key.SetValue(ValueName, command);
    }

    public static bool StartedMinimized =>
        Environment.GetCommandLineArgs().Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase);
}
