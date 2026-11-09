using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Onyx.Collector.Core;

namespace Onyx.App.UsageData;

/// <summary>Foreground app from the Win32 foreground window; idle time from the last input event.</summary>
public sealed partial class WindowsSources : IForegroundAppSource, IIdleTimeSource, IAppIconSource
{
    /// <summary>Lock screen and shell surfaces that mean "nobody is using an app".</summary>
    private static readonly HashSet<string> s_NotUsage = new(StringComparer.OrdinalIgnoreCase)
        { "LockApp.exe", "LogonUI.exe", "ShellExperienceHost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe" };

    private readonly int m_OwnProcessId = Environment.ProcessId;
    private readonly ConcurrentDictionary<string, string> m_PathByIdentifier = new();
    private readonly ConcurrentDictionary<int, (ForegroundApp? App, DateTime Started)> m_ByProcess = new();

    public ForegroundApp? GetForegroundApp()
    {
        var window = GetForegroundWindow();
        if (window == 0)
            return null;

        GetWindowThreadProcessId(window, out var pid);

        // UWP / packaged apps are hosted by ApplicationFrameHost; the real app owns a child window.
        if (ProcessName((int)pid) is "ApplicationFrameHost.exe")
            pid = FindHostedProcess(window, pid);

        return pid == 0 || pid == m_OwnProcessId ? null : Describe((int)pid);
    }

    public TimeSpan? GetIdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return null;

        // Both are milliseconds since boot; the unsigned difference handles the 49-day wrap of the 32-bit tick.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    public byte[]? GetIconPng(string appIdentifier) =>
        m_PathByIdentifier.TryGetValue(appIdentifier, out var path) ? IconExtractor.GetPng(path) : null;

    private ForegroundApp? Describe(int pid)
    {
        using var process = Process.GetProcessById(pid);
        var started = process.StartTime;

        // Process ids are reused; the start time tells a new process apart.
        if (m_ByProcess.TryGetValue(pid, out var cached) && cached.Started == started)
            return cached.App;

        var path = process.MainModule?.FileName;
        var exe = path is null ? process.ProcessName + ".exe" : Path.GetFileName(path);

        ForegroundApp? app = null;
        if (!s_NotUsage.Contains(exe))
        {
            var identifier = "windows:" + exe.ToLowerInvariant();
            string? name = null;
            if (path is not null)
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                name = NonEmpty(version.FileDescription) ?? NonEmpty(version.ProductName);
                m_PathByIdentifier[identifier] = path;
            }

            app = new ForegroundApp(identifier, name ?? process.ProcessName);
        }

        m_ByProcess[pid] = (app, started);
        return app;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static uint FindHostedProcess(nint frame, uint hostPid)
    {
        uint found = 0;
        EnumChildWindows(frame, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var childPid);
            if (childPid == hostPid)
                return true;
            found = childPid;
            return false;
        }, 0);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);
}
