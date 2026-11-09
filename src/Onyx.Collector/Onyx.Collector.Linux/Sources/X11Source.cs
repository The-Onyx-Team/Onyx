using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Onyx.Collector.Core;

namespace Onyx.Collector.Linux.Sources;

/// <summary>
/// X11 sessions: active window from the EWMH <c>_NET_ACTIVE_WINDOW</c> property, idle time from the
/// XScreenSaver extension. Not used on Wayland, where X11 only sees XWayland windows.
/// </summary>
internal sealed unsafe partial class X11Source : IForegroundAppSource, IIdleTimeSource, IDisposable
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXss = "libXss.so.1";
    private const nuint XaWindow = 33;
    private const nuint XaCardinal = 6;

    private readonly DesktopEntries m_DesktopEntries;
    private readonly nint m_Display;
    private readonly nuint m_Root;
    private readonly nuint m_ActiveWindowAtom;
    private readonly nuint m_PidAtom;
    private readonly bool m_HasScreenSaver;

    public static bool IsAvailable =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "x11"
        && Environment.GetEnvironmentVariable("DISPLAY") is not null;

    public X11Source(DesktopEntries desktopEntries)
    {
        m_DesktopEntries = desktopEntries;

        // Xlib's default handler exits the process on errors such as BadWindow, which happen whenever a window
        // closes between two calls.
        XSetErrorHandler(&IgnoreError);

        m_Display = XOpenDisplay(null);
        if (m_Display == 0)
            throw new InvalidOperationException("Cannot open X display");

        m_Root = XDefaultRootWindow(m_Display);
        m_ActiveWindowAtom = XInternAtom(m_Display, "_NET_ACTIVE_WINDOW", 0);
        m_PidAtom = XInternAtom(m_Display, "_NET_WM_PID", 0);

        try
        {
            int eventBase, errorBase;
            m_HasScreenSaver = XScreenSaverQueryExtension(m_Display, &eventBase, &errorBase) != 0;
        }
        catch (DllNotFoundException)
        {
            m_HasScreenSaver = false;
        }
    }

    public ForegroundApp? GetForegroundApp()
    {
        var window = (nuint)ReadCardinal(m_Root, m_ActiveWindowAtom, XaWindow);
        if (window == 0)
            return null;

        var pid = (int)ReadCardinal(window, m_PidAtom, XaCardinal);
        var exe = ProcessInfo.GetExecutableName(pid);

        XClassHint hint;
        string? wmClass = null;
        if (XGetClassHint(m_Display, window, &hint) != 0)
        {
            wmClass = Marshal.PtrToStringUTF8(hint.ResClass);
            if (hint.ResName != 0) XFree(hint.ResName);
            if (hint.ResClass != 0) XFree(hint.ResClass);
        }

        var key = string.IsNullOrEmpty(wmClass) ? exe : wmClass;
        return key is null ? null : m_DesktopEntries.Resolve(key, exe);
    }

    public TimeSpan? GetIdleTime()
    {
        if (!m_HasScreenSaver)
            return null;

        var info = XScreenSaverAllocInfo();
        try
        {
            return XScreenSaverQueryInfo(m_Display, m_Root, info) != 0
                ? TimeSpan.FromMilliseconds(info->Idle)
                : null;
        }
        finally
        {
            XFree((nint)info);
        }
    }

    private nint ReadCardinal(nuint window, nuint property, nuint type)
    {
        nuint actualType, items, bytesAfter;
        int actualFormat;
        byte* data = null;

        var status = XGetWindowProperty(m_Display, window, property, 0, 1, 0, type,
            &actualType, &actualFormat, &items, &bytesAfter, &data);
        try
        {
            // Format-32 properties are returned as C longs.
            return status == 0 && data != null && items > 0 && actualFormat == 32 ? *(nint*)data : 0;
        }
        finally
        {
            if (data != null)
                XFree((nint)data);
        }
    }

    public void Dispose()
    {
        if (m_Display != 0)
            XCloseDisplay(m_Display);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IgnoreError(nint display, nint errorEvent) => 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public nint ResName;
        public nint ResClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public nuint Window;
        public int State;
        public int Kind;
        public nuint TilOrSince;
        public nuint Idle;
        public nuint EventMask;
    }

    [LibraryImport(LibX11)] private static partial nint XOpenDisplay(byte* name);
    [LibraryImport(LibX11)] private static partial int XCloseDisplay(nint display);
    [LibraryImport(LibX11)] private static partial nuint XDefaultRootWindow(nint display);
    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)] private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);
    [LibraryImport(LibX11)] private static partial int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint reqType, nuint* actualType, int* actualFormat, nuint* items, nuint* bytesAfter, byte** data);
    [LibraryImport(LibX11)] private static partial int XGetClassHint(nint display, nuint window, XClassHint* hint);
    [LibraryImport(LibX11)] private static partial int XFree(nint data);
    [LibraryImport(LibX11)] private static partial nint XSetErrorHandler(delegate* unmanaged[Cdecl]<nint, nint, int> handler);
    [LibraryImport(LibXss)] private static partial int XScreenSaverQueryExtension(nint display, int* eventBase, int* errorBase);
    [LibraryImport(LibXss)] private static partial XScreenSaverInfo* XScreenSaverAllocInfo();
    [LibraryImport(LibXss)] private static partial int XScreenSaverQueryInfo(nint display, nuint drawable, XScreenSaverInfo* info);
}
