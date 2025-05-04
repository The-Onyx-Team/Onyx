using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.Core.App;
using Onyx.App.Services;

namespace Onyx.App;

[Service]
public class UsageDataForegroundService : Service
{
    private Timer m_Timer = null;
    private int m_Id = (new object()).GetHashCode();
    int m_BadgeNumber = 0;

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var input = intent.GetStringExtra("inputExtra");
        var notificationIntent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(this, 0, notificationIntent, PendingIntentFlags.Immutable);
        var notification = new NotificationCompat.Builder(this, MainApplication.ChannelId)
            .SetContentTitle("Service started")
            .SetContentText(input)
            .SetSmallIcon(Resource.Drawable.abc_action_bar_item_background_material)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build();

        Task.Run(() =>
        {
            Console.WriteLine("Started service!");
            m_Timer = new Timer(TimerElapsed, null, 0, 60000);
        });
        
        return StartCommandResult.Sticky;
    }

    private void TimerElapsed(object? state)
    {
        AndroidServiceManager.IsRunning = true;
        Console.WriteLine("Sending data to server...");
    }
}