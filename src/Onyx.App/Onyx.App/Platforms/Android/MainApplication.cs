using Android.App;
using Android.Runtime;
using Onyx.App;
using Onyx.App.UsageData;

#if DEBUG
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();

        // Idempotent: keeps an existing schedule. The worker does nothing until the user has signed in and
        // granted usage access.
        UsageSyncWorker.Schedule(this);
    }
}
