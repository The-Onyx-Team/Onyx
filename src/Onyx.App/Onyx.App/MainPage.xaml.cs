namespace Onyx.App
{
    public partial class MainPage : ContentPage
    {
        private const string CachedBuildKey = "onyx_webview_build";

        public MainPage()
        {
            InitializeComponent();
#if ANDROID
            // The web view keeps its own HTTP cache of the app's pages, styles and scripts. After an update it could
            // keep using the old ones (wrong layout, outdated code), so the cache is cleared once per new build.
            blazorWebView.BlazorWebViewInitialized += (_, e) =>
            {
                var build = $"{AppInfo.Current.VersionString}+{AppInfo.Current.BuildString}";
                if (Preferences.Default.Get(CachedBuildKey, "") == build)
                    return;
                e.WebView.ClearCache(true);
                Preferences.Default.Set(CachedBuildKey, build);
            };
#endif
        }
    }
}
