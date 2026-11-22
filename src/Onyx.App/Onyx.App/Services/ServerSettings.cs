using System.Reflection;
using Microsoft.Extensions.Logging;
using Onyx.App.Services.Collection;
using Onyx.App.Shared.Services;

namespace Onyx.App.Services;

/// <summary>
/// The server address the user picked on the login page, kept in preferences. Until one is picked the build's
/// default is used: <c>-p:OnyxServerUrl=...</c> at build time, otherwise <see cref="ServerConnectionHelper"/>.
/// </summary>
public sealed class ServerSettings(IHttpClientFactory httpClientFactory, ILogger<ServerSettings> logger)
    : IServerSettings
{
    private const string ServerUrlKey = "onyx_server_url";

    /// <summary>The address a fresh install uses.</summary>
    public static string DefaultUrl { get; } =
        typeof(ServerSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "OnyxServerUrl" && !string.IsNullOrWhiteSpace(a.Value))?.Value?.TrimEnd('/')
        ?? ServerConnectionHelper.DefaultUrl;

    public bool IsConfigurable => true;

    public string ServerUrl => Preferences.Default.Get(ServerUrlKey, (string?)null) ?? DefaultUrl;

    public async Task<string?> SetServerUrlAsync(string url)
    {
        if (Normalize(url) is not { } server)
            return "Enter an address like https://onyx.example.com";

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync($"{server}/alive");
            if (!response.IsSuccessStatusCode)
                return $"{server} answered with {(int)response.StatusCode}. Is this an Onyx server?";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogWarning(e, "Server check for {Server} failed", server);
            if (e.ToString().Contains("Cleartext", StringComparison.OrdinalIgnoreCase))
                return "This build only allows https:// servers. Use HTTPS, or build with -p:OnyxPrivateNetwork=true.";
            return $"Can't reach {server}: {e.Message}";
        }

        if (server != ServerUrl)
        {
            Preferences.Default.Set(ServerUrlKey, server);
            // The device id was issued by the previous server.
            MauiCredentialStore.ForgetDevice();
        }

        return null;
    }

    public async Task<Onyx.Data.ApiSchema.ServerInfoDto?> GetInfoAsync()
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            return await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<Onyx.Data.ApiSchema.ServerInfoDto>(
                client, $"{ServerUrl}/api/info");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogInformation(e, "Server info of {Server} not available", ServerUrl);
            return null;
        }
    }

    /// <summary>Adds https:// when the scheme is missing and drops a trailing slash.</summary>
    internal static string? Normalize(string? url)
    {
        url = url?.Trim();
        if (string.IsNullOrEmpty(url))
            return null;
        if (!url.Contains("://"))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return null;

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}

/// <summary>
/// Sends requests to the server currently selected in <see cref="ServerSettings"/>. HttpClient doesn't allow
/// changing <see cref="HttpClient.BaseAddress"/> once it has sent a request, so clients use
/// <see cref="Placeholder"/> as their base address and this handler rewrites it.
/// </summary>
public sealed class ServerUrlHandler(ServerSettings settings) : DelegatingHandler
{
    public static readonly Uri Placeholder = new("http://onyx-server.invalid/");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is { } uri && uri.Host == Placeholder.Host)
            request.RequestUri = new Uri(settings.ServerUrl + uri.PathAndQuery);

        return base.SendAsync(request, ct);
    }
}
