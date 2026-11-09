using System.Diagnostics;
using System.Text.RegularExpressions;
using Onyx.Collector.Core;

namespace Onyx.Collector.Linux.Sources;

/// <summary>
/// Idle time from the desktop session, for Wayland sessions without a compositor-specific source:
/// GNOME's Mutter IdleMonitor, then the freedesktop ScreenSaver interface (KDE and others). A locked session
/// (logind LockedHint) always counts as idle.
/// </summary>
internal sealed partial class SessionIdleSource(IIdleTimeSource? fallback = null) : IIdleTimeSource
{
    private static readonly TimeSpan s_Locked = TimeSpan.MaxValue;
    private bool m_MutterFailed;
    private bool m_ScreenSaverFailed;

    public TimeSpan? GetIdleTime()
    {
        if (IsLocked())
            return s_Locked;

        if (!m_MutterFailed)
        {
            // Returns "(uint64 12345,)" in milliseconds.
            var output = Run("gdbus", "call --session --dest org.gnome.Mutter.IdleMonitor " +
                                      "--object-path /org/gnome/Mutter/IdleMonitor/Core " +
                                      "--method org.gnome.Mutter.IdleMonitor.GetIdletime");
            if (output is not null && Number().Match(output) is { Success: true } m)
                return TimeSpan.FromMilliseconds(ulong.Parse(m.Value));
            m_MutterFailed = true;
        }

        if (!m_ScreenSaverFailed)
        {
            // Returns "(uint32 12,)" in seconds.
            var output = Run("gdbus", "call --session --dest org.freedesktop.ScreenSaver " +
                                      "--object-path /org/freedesktop/ScreenSaver " +
                                      "--method org.freedesktop.ScreenSaver.GetSessionIdleTime");
            if (output is not null && Number().Match(output) is { Success: true } m)
                return TimeSpan.FromSeconds(ulong.Parse(m.Value));
            m_ScreenSaverFailed = true;
        }

        return fallback?.GetIdleTime();
    }

    private static bool IsLocked()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_ID") ?? "auto";
        return Run("loginctl", $"show-session {session} -p LockedHint --value")?.Trim() == "yes";
    }

    private static string? Run(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
