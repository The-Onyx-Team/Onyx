namespace Onyx.App;

public static class AndroidServiceManager
{
    public static bool IsRunning { get; set; }
    private static MainActivity? _mainActivity;

    public static void Init(MainActivity activity)
    {
        _mainActivity = activity;
    }

    public static void StartService()
    {
        _mainActivity?.StartService();
    }

    public static void StopService()
    {
        _mainActivity?.StopService();
        IsRunning = false;
    }
}