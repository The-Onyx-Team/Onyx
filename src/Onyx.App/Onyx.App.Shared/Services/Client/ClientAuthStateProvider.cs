using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.Collector.Core;

namespace Onyx.App.Shared.Services.Client;

/// <summary>
/// The signed-in user, from the access token kept by the <see cref="ICredentialStore"/>. The token is only read,
/// not validated: the server validates it on every request.
/// </summary>
public sealed class ClientAuthStateProvider(ICredentialStore store) : AuthenticationStateProvider
{
    private static readonly AuthenticationState s_Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var credentials = await store.LoadAsync();
        return credentials is null ? s_Anonymous : new AuthenticationState(Principal(credentials.AccessToken));
    }

    /// <summary>Call after signing in or out.</summary>
    public void Refresh() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public static ClaimsPrincipal Principal(string accessToken)
    {
        var claims = new List<Claim>();
        if (ReadPayload(accessToken) is { } payload)
        {
            if (payload.TryGetValue("nameid", out var id))
                claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
            if (payload.TryGetValue("unique_name", out var name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
                // The server puts the email address into the name claim.
                if (name.Contains('@'))
                    claims.Add(new Claim(ClaimTypes.Email, name));
            }
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Onyx"));
    }

    private static Dictionary<string, string>? ReadPayload(string jwt)
    {
        try
        {
            var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)));
            return doc.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        }
        catch (Exception e) when (e is FormatException or JsonException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
