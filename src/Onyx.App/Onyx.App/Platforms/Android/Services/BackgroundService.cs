using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using ServiceInfoFlags = Android.App.ServiceInfoFlags;

namespace Onyx.App.Services;

[Service]
public class BackgroundService : Service
{
    Timer? _timer;
    readonly int _myId = (new object()).GetHashCode();
    int _badgeNumber;
    private readonly IBinder _binder;
    Notification? _notification;

    public BackgroundService()
    {
        _binder = new LocalBinder(this);
    }

    public class LocalBinder : Binder
    {
        private readonly BackgroundService _service;

        public LocalBinder(BackgroundService service)
        {
            _service = service;
        }

        public BackgroundService GetService()
        {
            return _service;
        }
    }

    public override IBinder OnBind(Intent? intent)
    {
        return _binder;
    }

    [Obsolete("Obsolete")]
    public override StartCommandResult OnStartCommand(Intent? intent,
        StartCommandFlags flags, int startId)
    {
        var input = intent?.GetStringExtra("extra");

        var notificationIntent = new Intent(this, typeof(MainActivity));
        notificationIntent.SetAction("USER_TAPPED_NOTIFICATION");

        var pendingIntent = PendingIntent.GetActivity(this, 0, notificationIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        _notification = new NotificationCompat.Builder(this,
                MainApplication.ChannelId)
            .SetContentText(input)
            .SetSmallIcon(ResourceConstant.Drawable.notification_icon_background)
            .SetAutoCancel(false)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetContentIntent(pendingIntent)
            .SetContentTitle("Background Service")
            .SetNumber(_badgeNumber)
            .Build();
        
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
#pragma warning disable CA1416
            StartForeground(_myId, _notification, ServiceInfo.ForegroundServiceTypeDataSync);
#pragma warning restore CA1416
        }
        else
        {
            StartForeground(_myId, _notification);
        }

        Console.WriteLine("Starting Service...");
        
        _timer = new Timer(TimerElapsed, _notification, 0, 10000);

        return StartCommandResult.Sticky;
    }


    [Obsolete("Obsolete")]
    private void TimerElapsed(object? state)
    {
        AndroidServiceManager.IsRunning = true;
        _badgeNumber++;

        if (_notification != null)
        {
            _notification.Number = _badgeNumber;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
#pragma warning disable CA1416
                StartForeground(_myId, _notification, ServiceInfo.ForegroundServiceTypeDataSync);
#pragma warning restore CA1416
            }
            else
            {
                StartForeground(_myId, _notification);
            }
        }
    }
}