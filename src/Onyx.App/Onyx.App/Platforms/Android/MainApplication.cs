using Android.App;
using Android.OS;
using Android.Runtime;

namespace Onyx.App
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override void OnCreate()
        {
            base.OnCreate();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                if (GetSystemService(NotificationService) is NotificationManager manager)
                    manager.CreateNotificationChannel(new NotificationChannel(
                        "Service", 
                        "Service Notification", 
                        NotificationImportance.High));
            }
        }
    }
}
