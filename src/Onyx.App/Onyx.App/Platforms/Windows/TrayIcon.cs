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
    private static Microsoft.UI.Dispatching.DispatcherQueue? s_Dispatcher;

    public static void Create(Action show, Action quit)
    {
        if (s_Icon is not null)
            return;

        s_Dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico"); // generated from the MauiIcon
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

    /// <summary>A Windows notification from the tray icon (reminders, focus mode). Callable from any thread.</summary>
    /// <returns>False if there is no tray icon (yet).</returns>
    public static bool Notify(string title, string message)
    {
        if (s_Icon is not { } icon || s_Dispatcher is not { } dispatcher)
            return false;
        return dispatcher.TryEnqueue(() => icon.ShowNotification(title, message));
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
