using System.Diagnostics;
using System.Runtime.InteropServices;
using Onyx.Collector.Core.Browser;

// Started by the browser with the extension's origin as argument. Everything on stdout is protocol, so nothing
// is printed; failures just end the process, and the extension reconnects on its next report.
await using var relay = new NativeHostRelay(BrowserProcess.Find());
await relay.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
return 0;

/// <summary>Finds the browser that started this host, to match its reports to the browser's windows.</summary>
internal static partial class BrowserProcess
{
    /// <summary>Chrome on Windows starts hosts through cmd.exe; skip such launchers.</summary>
    private static readonly HashSet<string> s_Launchers = new(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "conhost.exe", "sh", "bash", "dash" };

    public static int Find()
    {
        var pid = ParentOf(Environment.ProcessId);
        for (var hops = 0; hops < 3 && pid > 0 && s_Launchers.Contains(NameOf(pid) ?? ""); hops++)
            pid = ParentOf(pid);
        return pid;
    }

    private static string? NameOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return OperatingSystem.IsWindows() ? process.ProcessName + ".exe" : process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int ParentOf(int pid) => OperatingSystem.IsWindows() ? WindowsParentOf(pid) : LinuxParentOf(pid);

    private static int LinuxParentOf(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return 0;
        }
    }

    private static unsafe int WindowsParentOf(int pid)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1)
            return 0;

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                if (entry.ProcessId == pid)
                    return (int)entry.ParentProcessId;
            return 0;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private const uint SnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
