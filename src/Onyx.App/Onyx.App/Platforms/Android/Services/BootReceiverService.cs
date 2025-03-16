using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace Onyx.App.Services;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiverService : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        Console.WriteLine("Device booted!");
        
        if (intent?.Action == Intent.ActionBootCompleted)
        {
            Toast.MakeText(context, "Boot completed event received", 
                ToastLength.Short)
                ?.Show();

            if (context != null)
            {
                var serviceIntent = new Intent(context, 
                    typeof(BackgroundService));

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
#pragma warning disable CA1416
                    context.StartForegroundService(serviceIntent);
#pragma warning restore CA1416
                }
                else
                {
                    context.StartService(serviceIntent);
                }
            }
        }
    }
}