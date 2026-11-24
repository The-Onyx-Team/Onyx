using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MudBlazor.Services;
using Onyx.App.Services;
using Onyx.App.Services.Auth;
using Onyx.App.Services.Collection;
using Onyx.App.Shared.Services;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Shared.Services.Usage;
#if ANDROID || WINDOWS
using Onyx.App.UsageData;
#endif
using Onyx.Collector.Core;

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

            // The server is picked at runtime (login page); ServerUrlHandler points every request at it.
            builder.Services.AddSingleton<ServerSettings>();
            builder.Services.AddSingleton<IServerSettings>(sp => sp.GetRequiredService<ServerSettings>());
            builder.Services.AddHttpClient();

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudServices();
            builder.Services.AddOnyxClient();
            builder.Services.AddSingleton<IStorage, MauiStorage>();
            builder.Services.AddSingleton<IFileSaver, MauiFileSaver>();
            builder.Services.AddSingleton<IIconFetcher, DirectIconFetcher>();

            // Authenticated API access shared by the dashboard and the collectors. It uses the session the login
            // stores and refreshes its tokens itself; the encryption key is stored with it.
            builder.Services.AddSingleton<AuthenticationService>();
            builder.Services.AddSingleton<MauiCredentialStore>();
            builder.Services.AddSingleton<ICredentialStore>(sp => sp.GetRequiredService<MauiCredentialStore>());
            builder.Services.AddSingleton<IVaultKeyOptions>(sp => sp.GetRequiredService<MauiCredentialStore>());
            builder.Services.AddSingleton(sp => new OnyxApiClient(
                new HttpClient(new ServerUrlHandler(sp.GetRequiredService<ServerSettings>())
                    { InnerHandler = new HttpClientHandler() })
                    { BaseAddress = ServerUrlHandler.Placeholder, Timeout = TimeSpan.FromSeconds(60) },
                sp.GetRequiredService<ICredentialStore>(),
                sp.GetRequiredService<ILogger<OnyxApiClient>>()));

            // Platform Service
            builder.Services.AddScoped<IPlatformService, MauiPlatformService>();

#if ANDROID || IOS || MACCATALYST
            // WebAuthenticator isn't available on Windows; the Windows app signs in with a password.
            builder.Services.AddSingleton<IExternalLoginLauncher, BrowserExternalLoginLauncher>();
#endif

#if ANDROID
            builder.Services.AddSingleton<AndroidUsageReader>();
            builder.Services.AddSingleton<AndroidUsageSync>();
            builder.Services.AddSingleton<AndroidReminders>();
            builder.Services.AddSingleton<IStatsService, AndroidStatsService>();
#elif WINDOWS
            builder.Services.AddSingleton<WindowsCollector>();
            builder.Services.AddSingleton<IStatsService>(sp => sp.GetRequiredService<WindowsCollector>());
            ConfigureWindows(builder);
#else
            builder.Services.AddSingleton<IStatsService, UnsupportedStatsService>();
#endif

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

#if WINDOWS
            app.Services.GetRequiredService<WindowsCollector>().Start();
#endif

            return app;
        }

#if WINDOWS
        /// <summary>
        /// Closing the window asks whether to quit or keep collecting in the tray. The app also registers itself to
        /// start hidden at sign-in, since usage is only collected while it runs.
        /// </summary>
        private static void ConfigureWindows(MauiAppBuilder builder)
        {
            builder.ConfigureLifecycleEvents(lifecycle =>
            {
                lifecycle.AddWindows(windows => windows.OnWindowCreated(window =>
                {
                    window.ExtendsContentIntoTitleBar = true;
                    var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

                    async void Quit()
                    {
                        WinUI.TrayIcon.Remove();
                        if (IPlatformApplication.Current?.Services.GetService<WindowsCollector>() is { } collector)
                            await collector.StopAsync();
                        Application.Current?.Quit();
                    }

                    void Show() => window.DispatcherQueue.TryEnqueue(() =>
                    {
                        appWindow.Show();
                        window.Activate();
                    });

                    WinUI.TrayIcon.Create(Show, Quit);
                    WinUI.Autostart.Register();

                    if (WinUI.Autostart.StartedMinimized)
                        appWindow.Hide();

                    appWindow.Closing += async (_, e) =>
                    {
                        e.Cancel = true;
                        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                        var quit = page is not null && await page.DisplayAlertAsync(
                            "Close Onyx",
                            "Onyx only records app usage while it is running. Keep it running in the notification area?",
                            "Quit", "Keep running");

                        if (quit)
                            Quit();
                        else
                            appWindow.Hide();
                    };
                }));
            });
        }
#endif
    }

    /// <summary>Platforms without a collector (iOS, Mac Catalyst).</summary>
    internal sealed class UnsupportedStatsService : IStatsService
    {
        private static readonly CollectionStatus s_Status = new(false, false, null, null, 0);
        public CollectionStatus GetStatus() => s_Status;
        public void OpenUsageAccessSettings() { }
        public Task<CollectionStatus> SyncNowAsync() => Task.FromResult(s_Status);
    }
}
