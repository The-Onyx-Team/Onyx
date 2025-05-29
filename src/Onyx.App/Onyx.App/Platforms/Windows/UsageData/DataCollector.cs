using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Services.Api;
using Onyx.App.Shared.Services.Usage;
using Onyx.Data.ApiSchema;

namespace Onyx.App.UsageData;

public class DataCollector
{
    private UsageApi m_Api;
    private AuthenticationStateProvider m_AuthStateProvider;

    public List<Stats> Stats { get; set; } = new();

    public DataCollector(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        m_Api = scope.ServiceProvider.GetRequiredService<UsageApi>();
        m_AuthStateProvider = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();

        new Thread(() =>
        {
            while (true)
            {
                FetchData();
                SyncData().Wait();
                Thread.Sleep(1000);
            }
        }).Start();
    }

    private async Task SyncData()
    {
        var state = await m_AuthStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
        var userName = state.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        
        if (userId is null)
            return;

        Console.WriteLine(userId);
        Console.WriteLine(userName);
        
        await m_Api.UploadDataAsync(1, Stats.Select(s => new UsageDto()
        {
            Action = s.Name,
            DeviceName = "Workstation",
            UserId = userId.Value,
            DeviceId = 0,
            UserName = userId.Value,
            Date = (DateTime)s.IntervalStart!,
            IconUrl = "",
            TimeSpan = s.TimeInForeground
        }).ToList());
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

    [DllImport("shell32.dll")]
    static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public IntPtr iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    const uint SHGFI_ICON = 0x000000100;
    const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    //   private async Task<IRandomAccessStream?> ConvertHIconToPngStreamAsync(IntPtr hIcon)
    // {
    //     var bitmapSource = new SoftwareBitmapSource();
    //     var iconBitmap = await IconBitmapHelper.FromHIconAsync(hIcon);
    //     if (iconBitmap == null)
    //         return null;
    //
    //     InMemoryRandomAccessStream stream = new();
    //     var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
    //     encoder.SetSoftwareBitmap(iconBitmap);
    //     await encoder.FlushAsync();
    //
    //     stream.Seek(0);
    //     return stream;
    // }

    private byte[] GetProcessIcon(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
                return [];

            SHFILEINFO shinfo = new();
            SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (shinfo.hIcon == IntPtr.Zero)
                return [];

            // var iconStream = await ConvertHIconToPngStreamAsync(shinfo.hIcon);
            // DestroyIcon(shinfo.hIcon);
            //
            // if (iconStream == null)
            //     return [];
            //
            // using var ms = new MemoryStream();
            // await iconStream.AsStream().CopyToAsync(ms);
            //
            // return ms.ToArray();

            using var icon = System.Drawing.Icon.FromHandle(shinfo.hIcon);
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            DestroyIcon(shinfo.hIcon);
            return ms.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private string GetProcessCategory(Process activeProcess)
    {
        return "";
    }
}