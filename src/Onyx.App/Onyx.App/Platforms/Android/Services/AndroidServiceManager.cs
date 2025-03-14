namespace Onyx.App.Services;

public static class AndroidServiceManager
{
    public static MainActivity? MainActivity { get; set; }
    public static bool IsRunning { get; set; }

    public static void Start()
    {
        if (MainActivity != null)
            MainActivity.Start();
    }

    public static void Stop()
    {
        if (MainActivity != null)
        {
            IsRunning = false;
            MainActivity.Stop();
        }
    }
}