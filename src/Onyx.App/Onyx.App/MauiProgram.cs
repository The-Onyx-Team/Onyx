using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
#if WINDOWS
using Microsoft.UI.Windowing;
using Onyx.App.Platforms.Windows.UsageData;
#endif
using MudBlazor.Services;
using Onyx.App.Services;
using Onyx.App.Services.Api;
using Onyx.App.Services.Auth;
using Onyx.App.Shared.Services;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.UsageData;

namespace Onyx.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

            #if WINDOWS
            builder.ConfigureLifecycleEvents(lifecycle =>
            {
                lifecycle.AddWindows(lifecycleBuilder => lifecycleBuilder.OnWindowCreated(window =>
                {
                    window.ExtendsContentIntoTitleBar = true;
                    var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

                    appWindow.Closing += async (s, e) =>
                    {
                        e.Cancel = true;
                        var result = await Application.Current?.MainPage?.DisplayAlert(
                            "App close",
                            "Do you really want to quit?",
                            "Close",
                            "Minimize to system tray")!;

                        if (result)
                        {
                            Application.Current?.Quit();
                        }

                        appWindow.Hide();
                    };
                }));
            });

            builder.Services.AddSingleton<DataCollector>();
            #endif

            builder.Services.AddHttpClient<HttpClientWrapper>();
            builder.Services.AddScoped<AuthApi>();

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudServices();
            builder.Services.AddSingleton<IStorage, MauiStorage>();

            builder.Services.AddSingleton<AuthenticationService>();
            builder.Services.AddScoped<AuthenticationStateProvider, MauiAuthenticationStateProvider>();
            builder.Services.AddAuthorizationCore();
            builder.Services.AddSingleton<IUserManager, UserManager>();

            builder.Services.AddSingleton<IStatsService, UsageStatsService>();
            builder.Services.AddSingleton<IStatsHelper, StatsHelper>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}