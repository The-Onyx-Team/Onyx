using System.Diagnostics;
using System.Runtime.InteropServices;
using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.Platforms.Windows.UsageData;

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

    public static string GetActiveWindowProcess()
    {
        IntPtr hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);
        var process = Process.GetProcessById((int)pid);
        return process.ProcessName;
    }

    private void FetchData()
    {
        var data = new List<Stats>();

        var processes = Process.GetProcesses();
        foreach (var process in processes)
        {
            try
            {
                data.Add(new Stats()
                {
                    Category = "",
                    Icon = [],
                    IntervalEnd = DateTime.Now,
                    IntervalStart = DateTime.Now,
                    LastTimeUsed = DateTime.Now,
                    Name = process.ProcessName,
                    TimeInForeground = process.TotalProcessorTime,
                    TimeVisible = process.TotalProcessorTime
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        var ap = GetActiveWindowProcess();
        Console.WriteLine(ap);
        Stats = data;
    }
}