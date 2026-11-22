using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace Onyx.App
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // The system bars sit on the app's dark background (PageBackgroundColor), so their icons must be light,
            // whatever the phone's theme.
            if (Window is { } window)
            {
                var insets = WindowCompat.GetInsetsController(window, window.DecorView);
                insets.AppearanceLightStatusBars = false;
                insets.AppearanceLightNavigationBars = false;
            }
        }
    }
}
