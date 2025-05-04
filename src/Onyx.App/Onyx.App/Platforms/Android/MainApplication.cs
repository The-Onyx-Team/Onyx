using Android.App;
using Android.Runtime;

namespace Onyx.App
{
#if DEBUG
    [Application(UsesCleartextTraffic = true)]
#else
    [Application]
#endif
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }
namespace Onyx.App;

[Application]
public class MainApplication : MauiApplication
{
    public static readonly string ChannelId = "bgChannel";
    
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
#pragma warning disable CA1416
            var serviceChannel =
                new NotificationChannel(
                    ChannelId, 
                    "Background Service Channel", 
                    NotificationImportance.Low);
            
            if (GetSystemService(NotificationService)
                is NotificationManager manager)
            {
                manager.CreateNotificationChannel(serviceChannel);
            }
#pragma warning restore CA1416
        }
    }
}
