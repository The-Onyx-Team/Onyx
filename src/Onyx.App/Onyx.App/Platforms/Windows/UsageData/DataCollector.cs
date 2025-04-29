using System.Diagnostics;
using System.Runtime.InteropServices;
using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.UsageData;

public class DataCollector
{
    public List<Stats> Stats { get; set; } = new();

    public DataCollector()
    {
        new Thread(() =>
        {
            while (true)
            {
                FetchData();
                Thread.Sleep(1000);
            }
        }).Start();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static Process GetActiveWindowProcess()
    {
        IntPtr hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);
        var process = Process.GetProcessById((int)pid);
        return process;
    }

    private void FetchData()
    {
        var activeProcess = GetActiveWindowProcess();

        var entry = Stats.LastOrDefault(x => x.Name == activeProcess.ProcessName);

        if (entry is null || entry.IntervalEnd < DateTime.Now.AddSeconds(-10))
        {
            CreateNewEntry(activeProcess);
            return;
        }

        entry.IntervalEnd = DateTime.Now;
        entry.LastTimeUsed = DateTime.Now;
        entry.TimeVisible += TimeSpan.FromSeconds(1);
        entry.TimeInForeground += TimeSpan.FromSeconds(1);
    }

    private void CreateNewEntry(Process activeProcess)
    {
        Stats.Add(new Stats
        {
            Category = GetProcessCategory(activeProcess),
            Icon = GetProcessIcon(activeProcess),
            IntervalEnd = DateTime.Now,
            IntervalStart = DateTime.Now,
            LastTimeUsed = DateTime.Now,
            Name = activeProcess.ProcessName,
            TimeInForeground = TimeSpan.FromSeconds(1),
            TimeVisible = TimeSpan.FromSeconds(1)
        });
    }

    private byte[] GetProcessIcon(Process activeProcess)
    {
        return [];
    }

    private string GetProcessCategory(Process activeProcess)
    {
        return "";
    }
}