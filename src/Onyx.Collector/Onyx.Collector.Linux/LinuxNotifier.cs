using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Onyx.Collector.Core.Reminders;

namespace Onyx.Collector.Linux;

/// <summary>
/// Desktop notifications through the freedesktop notification service, with <c>notify-send</c> (libnotify) or,
/// without it, <c>gdbus</c>. Every desktop with a notification daemon shows them (GNOME, KDE, mako, dunst, ...).
/// </summary>
public sealed class LinuxNotifier(ILogger<LinuxNotifier> logger) : INudgeNotifier
{
    private bool m_Warned;

    public void Show(Nudge nudge)
    {
        if (TryRun("notify-send", ["--app-name=Onyx", "--icon=onyx", "--urgency=normal", nudge.Title, nudge.Message]))
            return;

        // org.freedesktop.Notifications.Notify(app, replaces_id, icon, summary, body, actions, hints, timeout)
        if (TryRun("gdbus", ["call", "--session", "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications", "--method", "org.freedesktop.Notifications.Notify",
                "Onyx", "0", "onyx", nudge.Title, nudge.Message, "[]", "{}", "10000"]))
            return;

        if (!m_Warned)
            logger.LogWarning("Could not show a notification: install notify-send (libnotify) for reminders and focus mode");
        m_Warned = true;
        logger.LogInformation("{Title}: {Message}", nudge.Title, nudge.Message);
    }

    private static bool TryRun(string program, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null)
                return false;
            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // not installed
        }
    }
}
