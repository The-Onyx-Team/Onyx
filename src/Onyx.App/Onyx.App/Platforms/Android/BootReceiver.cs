using Android.App;
using Android.Content;
using Android.Widget;
using AndroidX.Core.Content;

namespace Onyx.App;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiver1 : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == Intent.ActionBootCompleted)
        {
            Toast.MakeText(context, "Boot completed!", ToastLength.Short)?.Show();
            var serviceIntent = new Intent(context ?? throw new ArgumentNullException(nameof(context)), typeof(UsageDataForegroundService));
            ContextCompat.StartForegroundService(context, serviceIntent);
        }
    }
}