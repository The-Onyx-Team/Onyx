using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Onyx.App;

/// <summary>Receives onyx://external from the browser at the end of a Google/Microsoft/GitHub sign-in and hands it
/// to <see cref="Microsoft.Maui.Authentication.WebAuthenticator"/>.</summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "onyx", DataHost = "external")]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity;
