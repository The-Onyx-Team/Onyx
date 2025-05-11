using Android.Content;
using Android.Provider;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Services.Api;
using Onyx.App.Services.Auth;
using Onyx.App.Shared.Services.Usage;
using Application = Android.App.Application;

namespace Onyx.App.Services;

public class UsageStatsService(IServiceProvider provider) : IStatsService
{
    private readonly IStatsHelper m_StatsService = provider.GetRequiredService<IStatsHelper>();
    private readonly HttpClientWrapper m_HttpClientWrapper = provider.GetRequiredService<HttpClientWrapper>();
    private readonly AuthenticationStateProvider m_AuthProvider = provider.GetRequiredService<AuthenticationStateProvider>();

    public void OpenUsageAccessSettings()
    {
        var intent = new Intent(Settings.ActionUsageAccessSettings);
        intent.SetFlags(ActivityFlags.NewTask);
        Application.Context.ApplicationContext?.StartActivity(intent);
    }

    public async Task<bool> UploadData()
    {
        var authState = await m_AuthProvider.GetAuthenticationStateAsync();
        var token = await authState.User.GetAuthTokenAsync();
        if (token is null) return false;
        
        m_HttpClientWrapper.SetAuthToken(token);
        long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long startTime = endTime - SyncIntervalHelper.SyncIntervalInMilliseconds;
        var data = m_StatsService.GetUsageStatsTimeIntervalMilliseconds(startTime, endTime);

        var result = await m_HttpClientWrapper.PostAsync<bool>(
            "/api/data/usage/upload",
            data!
        );

        return result is { IsT0: true, AsT0: true };
    }
}