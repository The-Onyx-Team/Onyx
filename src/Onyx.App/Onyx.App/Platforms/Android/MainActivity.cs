using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Onyx.App.Services;

namespace Onyx.App
{
    [Activity(
        Theme = "@style/Maui.SplashTheme", 
        MainLauncher = true, 
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public void StartService()
        {
            var serviceIntent = new Intent(this, typeof(UsageDataForegroundService));
            serviceIntent.PutExtra("inputExtra", "onyx-foreground-service");
            StartService(serviceIntent);
            Console.WriteLine("Starting service...");
        }

        public void StopService()
        {
            var serviceIntent = new Intent(this, typeof(UsageDataForegroundService));
            StartService(serviceIntent);
            Console.WriteLine("Stopping service...");
        }
        
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            AndroidServiceManager.Init(this);
        }

    }
}
