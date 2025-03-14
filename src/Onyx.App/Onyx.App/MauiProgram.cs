using Microsoft.Extensions.Logging;
using Onyx.App.Shared.Services.Usage;
#if ANDROID
using Onyx.App.Services;
using Onyx.App.UsageData;
#endif

namespace Onyx.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
#if ANDROID
            builder.Services.AddSingleton<IStatsService, UsageStatsService>();
            builder.Services.AddSingleton<IStatsHelper, StatsHelper>();
#endif
#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
