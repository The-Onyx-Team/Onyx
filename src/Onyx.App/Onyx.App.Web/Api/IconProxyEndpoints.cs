using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;

namespace Onyx.App.Web.Api;

/// <summary>
/// Downloads a picture from a URL for the browser, which can't fetch most other sites itself (CORS). The apps
/// download directly and don't use this. Only public addresses are fetched: the server must not become a way
/// into the network it runs in.
/// </summary>
public static class IconProxyEndpoints
{
    public const int MaxBytes = 1024 * 1024;

    private static readonly HashSet<string> s_ImageTypes =
    [
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml", "image/x-icon",
        "image/vnd.microsoft.icon", "image/bmp"
    ];

    private static readonly HttpClient s_Http = new(new SocketsHttpHandler
    {
        // Every connection, including redirects, goes through this check (DNS rebinding included).
        ConnectCallback = ConnectPublicOnlyAsync,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "Onyx icon fetcher" } }
    };

    public static IEndpointRouteBuilder MapIconProxy(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/icon-proxy", FetchAsync)
            .RequireAuthorization(Program.ApiPolicy)
            .RequireRateLimiting(Program.AuthRateLimit);
        return routes;
    }

    public static async Task<IResult> FetchAsync([FromQuery] string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Results.BadRequest("Enter an http or https address.");

        try
        {
            using var response = await s_Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return Results.BadRequest($"The site answered {(int)response.StatusCode}.");

            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (type is null || !s_ImageTypes.Contains(type))
                return Results.BadRequest("That address is not a picture (PNG, JPEG, GIF, WebP, SVG or ICO).");
            if (response.Content.Headers.ContentLength > MaxBytes)
                return Results.BadRequest("The picture is larger than 1 MB.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                    return Results.BadRequest("The picture is larger than 1 MB.");
            }

            return Results.File(buffer.ToArray(), type);
        }
        catch (HttpRequestException e) when (e.InnerException is BlockedAddressException)
        {
            return Results.BadRequest("Only public internet addresses can be fetched.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return Results.BadRequest("The picture could not be downloaded.");
        }
    }

    private static async ValueTask<Stream> ConnectPublicOnlyAsync(SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var address = addresses.FirstOrDefault(IsPublic) ?? throw new BlockedAddressException();

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Whether an address is on the public internet (not loopback, private, link-local, ...).</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10
                     || b[0] == 0
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // carrier-grade NAT, also Tailscale
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                     || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                     || (b[0] & 0xFE) == 0xFC // unique local fc00::/7
                     || address.IsIPv6Teredo);
        }

        return false;
    }

    private sealed class BlockedAddressException() : Exception("Address not allowed");
}
