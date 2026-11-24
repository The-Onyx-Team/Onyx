using System.Security.Cryptography;
using Onyx.App.Shared.Services;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Services.Auth;

/// <summary>
/// External sign-in in the system browser (<see cref="WebAuthenticator"/>): the server sends the browser to
/// <see cref="AppExternalLogin.CallbackUrl"/> at the end, which Android hands back to the app
/// (<c>WebAuthenticationCallbackActivity</c>). A random verifier, whose hash goes along at the start, makes sure only
/// this app can redeem the result, even if another app registered the same address.
/// </summary>
public sealed class BrowserExternalLoginLauncher(IServerSettings server) : IExternalLoginLauncher
{
    public async Task<string?> SignInAsync(string provider)
    {
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new Uri($"{server.ServerUrl.TrimEnd('/')}{AppExternalLogin.StartPath}" +
                            $"?provider={Uri.EscapeDataString(provider)}&challenge={AppExternalLogin.Challenge(verifier)}");

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = start,
                CallbackUrl = new Uri(AppExternalLogin.CallbackUrl),
                PrefersEphemeralWebBrowserSession = true
            });
        }
        catch (TaskCanceledException)
        {
            return null;
        }

        // The result carries the same values the web app finds in its address (code / register+ticket / error).
        var values = result.Properties
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
            .Append($"verifier={verifier}");
        return "/account/external#" + string.Join("&", values);
    }
}
