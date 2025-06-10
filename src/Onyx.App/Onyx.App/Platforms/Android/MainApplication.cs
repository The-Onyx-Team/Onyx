using Android.App;
using Android.OS;
using Android.Runtime;
using Onyx.App;

#if DEBUG
    [Application(UsesCleartextTraffic = true)]
#else
    [Application]
#endif
public class MainApplication : MauiApplication
{
    public static string ChannelId { get; } = "onyx-channel";
    
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();

        Console.WriteLine("Creating channel...");

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
#pragma warning disable CA1416
            var serviceChannel = new NotificationChannel(ChannelId, "onyx-channel", NotificationImportance.High);
            if (GetSystemService(NotificationService) is NotificationManager manager)
                manager.CreateNotificationChannel(serviceChannel);
#pragma warning restore CA1416
        }
    }
}
