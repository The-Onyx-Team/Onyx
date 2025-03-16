using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Onyx.App.Services;
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
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

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
