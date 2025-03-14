using Android.App;
using Android.Content;
using Android.Content.PM;
using Onyx.App.Services;

namespace Onyx.App
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public MainActivity()
        {
            AndroidServiceManager.MainActivity = this;
        }

        public void Start()
        {
            StartService(new Intent(this, typeof(BackgroundService)));
        }

        public void Stop()
        {
            StopService(new Intent(this, typeof(BackgroundService)));
        }
    }
}
