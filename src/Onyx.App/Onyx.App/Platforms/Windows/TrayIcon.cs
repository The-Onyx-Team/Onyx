using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Onyx.App.WinUI;

/// <summary>
/// Notification-area icon. Closing the window can hide the app instead of quitting (collection keeps running);
/// the icon brings it back or quits for real.
/// </summary>
internal static class TrayIcon
{
    private static TaskbarIcon? s_Icon;

    public static void Create(Action show, Action quit)
    {
        if (s_Icon is not null)
            return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
        s_Icon = new TaskbarIcon
        {
            ToolTipText = "Onyx - collecting app usage",
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
            NoLeftClickDelay = true,
            LeftClickCommand = new XamlUICommand().With(show),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Text = "Open Onyx", Command = new XamlUICommand().With(show) },
                    new MenuFlyoutItem { Text = "Quit", Command = new XamlUICommand().With(quit) }
                }
            }
        };
        s_Icon.ForceCreate();
    }

    public static void Remove()
    {
        s_Icon?.Dispose();
        s_Icon = null;
    }

    private static XamlUICommand With(this XamlUICommand command, Action action)
    {
        command.ExecuteRequested += (_, _) => action();
        return command;
    }
}
