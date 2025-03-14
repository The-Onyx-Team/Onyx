using System.Diagnostics.Metrics;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace Onyx.App.Services;

[Service]
public class BackgroundService : Service
{
    private Timer _timer;
    private int _counter;
    
    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var pendingIntent = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(this, "Service")
            .SetContentText("Sending Data...")
            .SetSmallIcon(Microsoft.Maui.Controls.Resource.Drawable.notification_icon_background)
            .SetContentIntent(pendingIntent);

        _timer = new Timer(TimerElapsed, notification, 0, 60);
        
        return StartCommandResult.Sticky;
    }

    private void TimerElapsed(object? state)
    {
        _counter++;
        var notification = (NotificationCompat.Builder)state!;
        notification
            .SetNumber(_counter)
            .SetContentTitle("Background Service");
        StartForeground(1, notification.Build());
    }
}