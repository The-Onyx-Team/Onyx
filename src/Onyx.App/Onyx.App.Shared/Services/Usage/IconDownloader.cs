using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>
/// Downloads an app icon from what a user pastes: a picture's address, or a website (whose icon is used:
/// <c>apple-touch-icon</c>, the largest <c>icon</c>, or <c>/favicon.ico</c>). Used directly by the apps and by the
/// server's icon proxy for the browser, which passes an <see cref="HttpClient"/> that only reaches public addresses.
/// </summary>
public static partial class IconDownloader
{
    public const int MaxBytes = 1024 * 1024;
    private const int MaxPageBytes = 512 * 1024;

    /// <summary>Some sites refuse clients without a browser-like User-Agent.</summary>
    public const string UserAgent = "Mozilla/5.0 (compatible; Onyx icon fetcher; +https://github.com/The-Onyx-Team/Onyx)";

    /// <summary>The address as typed, with <c>https://</c> added when there is no scheme.</summary>
    /// <exception cref="InvalidOperationException">With a message for the user.</exception>
    public static Uri Normalize(string input)
    {
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                                                                 || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("Enter a web address, e.g. https://example.com/icon.png or example.com.");
        return uri;
    }

    /// <returns>The picture and its media type.</returns>
    /// <exception cref="InvalidOperationException">With a message for the user.</exception>
    public static async Task<(byte[] Data, string Type)> DownloadAsync(HttpClient http, Uri uri,
        CancellationToken ct = default)
    {
        var (data, type, finalUri) = await GetAsync(http, uri, ct);
        if (ImageType(data, type) is { } imageType)
            return (data, imageType);

        if (type is not ("text/html" or "application/xhtml+xml"))
            throw new InvalidOperationException("That address is not a picture (PNG, JPEG, GIF, WebP, SVG or ICO) or a website.");

        // A website: its own icon, best first.
        var page = Encoding.UTF8.GetString(data);
        foreach (var candidate in IconLinks(page, finalUri).Append(new Uri(finalUri, "/favicon.ico")).Distinct().Take(4))
        {
            try
            {
                var (icon, iconType, _) = await GetAsync(http, candidate, ct);
                if (ImageType(icon, iconType) is { } found)
                    return (icon, found);
            }
            catch (InvalidOperationException)
            {
                // try the next one
            }
        }

        throw new InvalidOperationException("That website has no icon Onyx could find. Paste the address of a picture instead.");
    }

    private static async Task<(byte[] Data, string? Type, Uri Uri)> GetAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "image/*,text/html;q=0.8,*/*;q=0.5");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("The site took too long to answer.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The site answered {(int)response.StatusCode} ({response.ReasonPhrase}).");

            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var limit = type is "text/html" or "application/xhtml+xml" ? MaxPageBytes : MaxBytes;
            if (response.Content.Headers.ContentLength > limit && limit == MaxBytes)
                throw new InvalidOperationException("The picture is larger than 1 MB.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length <= limit)
                    continue;
                if (limit == MaxBytes)
                    throw new InvalidOperationException("The picture is larger than 1 MB.");
                break; // the head of a page is enough to find its icon
            }

            return (buffer.ToArray(), type, response.RequestMessage?.RequestUri ?? uri);
        }
    }

    /// <summary>The picture's media type from its first bytes, or from the header for SVG; null if not a picture.</summary>
    public static string? ImageType(byte[] data, string? declared)
    {
        bool Starts(params byte[] magic) => data.Length >= magic.Length && data.AsSpan(0, magic.Length).SequenceEqual(magic);

        if (Starts(0x89, 0x50, 0x4E, 0x47)) return "image/png";
        if (Starts(0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (Starts(0x47, 0x49, 0x46, 0x38)) return "image/gif";
        if (Starts(0x00, 0x00, 0x01, 0x00)) return "image/x-icon";
        if (Starts(0x42, 0x4D)) return "image/bmp";
        if (data.Length > 12 && Starts(0x52, 0x49, 0x46, 0x46) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        if (declared == "image/svg+xml" || (declared is null or "text/xml" or "application/xml" or "text/plain"
                                            && Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 512)).Contains("<svg", StringComparison.OrdinalIgnoreCase)))
            return "image/svg+xml";
        return null;
    }

    /// <summary>Icon links of a page, best first: apple-touch-icon (180 px), then larger icons, then SVG.</summary>
    public static IEnumerable<Uri> IconLinks(string html, Uri page)
    {
        var baseUri = page;
        if (BaseTag().Match(html) is { Success: true } b && Attribute(b.Value, "href") is { } baseHref
                                                         && Uri.TryCreate(page, WebUtility.HtmlDecode(baseHref), out var parsedBase))
            baseUri = parsedBase;

        var candidates = new List<(Uri Uri, int Score)>();
        foreach (Match link in LinkTag().Matches(html))
        {
            var rel = Attribute(link.Value, "rel")?.ToLowerInvariant() ?? "";
            var href = Attribute(link.Value, "href");
            if (href is null || !rel.Contains("icon") || rel.Contains("mask-icon"))
                continue;
            if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out var uri) || uri.Scheme is not ("http" or "https"))
                continue;

            var size = Attribute(link.Value, "sizes") is { } sizes && SizeValue().Match(sizes) is { Success: true } m
                ? int.Parse(m.Groups[1].Value)
                : 0;
            var score = rel.Contains("apple-touch-icon") ? 1000 + size
                : href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? 500
                : size;
            candidates.Add((uri, score));
        }

        return candidates.OrderByDescending(c => c.Score).Select(c => c.Uri);
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Success ? match.Groups[1].Value
            : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value : null;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"<base\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseTag();

    [GeneratedRegex(@"(\d+)x\d+")]
    private static partial Regex SizeValue();
}
