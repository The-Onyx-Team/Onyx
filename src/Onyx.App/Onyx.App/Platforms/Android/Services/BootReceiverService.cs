using Android.App;
using Android.Content;
using Android.Widget;
using AndroidX.Core.Content;

namespace Onyx.App.Services;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiverService : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent.Action == Intent.ActionBootCompleted && context != null)
        {
            Toast.MakeText(context, "Toast showing as boot completed!", ToastLength.Short).Show();
            ContextCompat.StartForegroundService(context, new Intent(context, typeof(BackgroundService)));
        }
    }
}