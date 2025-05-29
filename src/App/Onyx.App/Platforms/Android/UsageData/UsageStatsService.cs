using Android.Content;
using Android.Provider;
using Onyx.App.Shared.Services.Usage;
using Application = Android.App.Application;

namespace Onyx.App.UsageData;

public class UsageStatsService : IStatsService
{
    public void OpenUsageAccessSettings()
    {
        var intent = new Intent(Settings.ActionUsageAccessSettings);
        intent.SetFlags(ActivityFlags.NewTask);
        Application.Context.ApplicationContext?.StartActivity(intent);
    }
}