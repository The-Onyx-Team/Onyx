using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Onyx.App.Shared.Services.Usage;

namespace Onyx.App;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
public class UsageDataForegroundService : Service
{
    private Timer? m_Timer;
    private readonly int m_Id = (new object()).GetHashCode();
    public int BadgeNumber { get; } = 0;

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var input = intent?.GetStringExtra("inputExtra");
        var notificationIntent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(this, 0, notificationIntent, PendingIntentFlags.Immutable);
        var notification = new NotificationCompat.Builder(this, MainApplication.ChannelId)
            .SetContentTitle("Service started")
            .SetContentText(input)
            .SetSmallIcon(ResourceConstant.Drawable.abc_action_bar_item_background_material)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build();
        
#pragma warning disable CA1416
        StartForeground(m_Id, notification, ForegroundService.TypeDataSync);
#pragma warning restore CA1416

        Task.Run(() =>
        {
            Console.WriteLine("Started service!");
            m_Timer = new Timer(TimerElapsed, null, 0, SyncIntervalHelper.SyncIntervalInMilliseconds);
        });
        
        return StartCommandResult.Sticky;
    }

    private async void TimerElapsed(object? state)
    {
        AndroidServiceManager.IsRunning = true;
        Console.WriteLine("Sending data to server...");
        
        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var serviceProvider = MauiApplication.Current.Services;
#pragma warning restore CS0618 // Type or member is obsolete
            var usageStatsService = serviceProvider.GetService<IStatsService>();

            var nullCheck = usageStatsService != null;
            Console.WriteLine((bool)nullCheck);
            
            var result = await usageStatsService.UploadData();
            if (!result) throw new Exception("Failed to upload data");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uploading usage data: {ex.Message}");
        }
    }
}