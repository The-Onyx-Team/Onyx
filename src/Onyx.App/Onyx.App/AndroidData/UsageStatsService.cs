using Android.Content;
using Android.Provider;

namespace Onyx.App.AndroidData;

public class UsageStatsService
{
    public void OpenUsageAccessSettings()
    {
        var intent = new Intent(Settings.ActionUsageAccessSettings);
        intent.SetFlags(ActivityFlags.NewTask);
        Android.App.Application.Context.ApplicationContext.StartActivity(intent);
    }
}