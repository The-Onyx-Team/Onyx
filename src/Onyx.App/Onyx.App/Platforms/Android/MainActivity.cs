using Android.App;
using Android.Content;
using Android.Content.PM;
using Onyx.App.Services;

namespace Onyx.App
{
    [Activity(
        Theme = "@style/Maui.SplashTheme", 
        MainLauncher = true, 
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public MainActivity()
        {
            AndroidServiceManager.MainActivity = this;
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            // Handle the intent that you received
            ProcessIntent(intent);
        }

        private void ProcessIntent(Intent? intent)
        {
            // Extract data from the intent and use it
            // For example, you can check for a specific action or extract extras
            if (intent != null)
            {
                // Example: checking for a specific action
                var action = intent.Action;
                if (action == "USER_TAPPED_NOTIFICATION")
                {
                    // Handle the specific action
                }
            }
        }

        public void StartService()
        {
            var serviceIntent = new Intent(this, typeof(BackgroundService));
            serviceIntent.PutExtra("extra", "Background Service");
            StartService(serviceIntent);
        }

        public void StopService()
        {
            var serviceIntent = new Intent(this, typeof(BackgroundService));
            StopService(serviceIntent);
        }
    }
}
