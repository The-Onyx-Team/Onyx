using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Documents;
using Onyx.App.Shared.Services.Goals;
using Onyx.App.Shared.Services.Reports;
using Onyx.App.Shared.Services.Social;
using Onyx.App.Shared.Services.Usage;

namespace Onyx.App.Shared.Services;

public static class OnyxClientServices
{
    /// <summary>
    /// The services the shared UI uses, over the API, with decryption on the device. The host adds what differs
    /// per platform: <c>OnyxApiClient</c> (with its HttpClient and crypto), <c>ICredentialStore</c>,
    /// <see cref="IStorage"/>, <see cref="IPlatformService"/>, <see cref="IServerSettings"/>,
    /// <see cref="IStatsService"/>, <see cref="IVaultKeyOptions"/>, <see cref="IFileSaver"/> and
    /// <see cref="IIconFetcher"/>.
    /// </summary>
    public static IServiceCollection AddOnyxClient(this IServiceCollection services)
    {
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OnyxJs>();
        services.AddScoped<ClientAuthStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ClientAuthStateProvider>());
        services.AddScoped<ClientTimeZone>();

        services.AddScoped<VaultService>();
        services.AddScoped<MigrationService>();
        services.AddScoped<IUserManager, ClientUserManager>();
        services.AddScoped<IUserProvider, ClientUserProvider>();
        services.AddScoped<ITwoFactorService, ClientTwoFactorService>();
        services.AddScoped<PrivateProfileService>();
        services.AddScoped<Focus.FocusService>();
        services.AddScoped<Reports.CustomReportService>();
        services.AddScoped<IProfileService, ApiProfileService>();

        services.AddScoped<IDeviceManager, ApiDeviceManager>();
        services.AddScoped<AppCatalog>();
        services.AddScoped<E2EUsageDataService>();
        services.AddScoped<IUsageDataService>(sp => sp.GetRequiredService<E2EUsageDataService>());
        services.AddScoped<IWebsiteSettings, E2EWebsiteSettings>();
        services.AddScoped<IReportService, ClientReportService>();
        services.AddScoped<GoalService>();
        services.AddScoped<SocialService>();
        services.AddScoped<PersonalDataService>();
        return services;
    }
}
