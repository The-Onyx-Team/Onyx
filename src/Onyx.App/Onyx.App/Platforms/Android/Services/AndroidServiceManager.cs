using Android.Content;
using Android.OS;

namespace Onyx.App.Services;

public static class AndroidServiceManager
{
    public static MainActivity? MainActivity { get; set; }

    public static bool IsRunning { get; set; }

    public static void StartMyService()
    {
        if (MainActivity == null) return;
        var serviceIntent = new Intent(MainActivity, typeof(BackgroundService));
        serviceIntent.PutExtra("extra", "Background Service Running");
    
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
#pragma warning disable CA1416
            MainActivity.StartForegroundService(serviceIntent);
            Console.WriteLine("Start");
#pragma warning restore CA1416
        }
        else
        {
            MainActivity.StartService(serviceIntent);
            Console.WriteLine("Start");
        }
    }
    
    public static void StopMyService()
    {
        if (MainActivity == null) return;
        var serviceIntent = new Intent(MainActivity, typeof(BackgroundService));
        MainActivity.StopService(serviceIntent);
        IsRunning = false;
    }

}