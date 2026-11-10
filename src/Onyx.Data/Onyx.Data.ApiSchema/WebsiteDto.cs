using System.Globalization;
using System.Text.RegularExpressions;

namespace Onyx.Data.ApiSchema;

/// <summary>Domains the user never wants recorded. Subdomains are included: <c>example.com</c> also excludes
/// <c>mail.example.com</c>.</summary>
public record WebsiteExclusionsDto(List<string> Domains);

/// <summary>
/// Website domains as Onyx records them: lower case, ASCII (punycode), without <c>www.</c>, port, path or query.
/// Only the domain is ever stored; full addresses can contain search terms, tokens and other private details.
/// </summary>
public static partial class WebDomain
{
    public const int MaxLength = 253;

    private static readonly IdnMapping s_Idn = new();

    /// <summary>Normalizes a domain or a URL to the stored form.</summary>
    /// <returns>Null when the input is not an http(s) URL or a valid domain name.</returns>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var host = value.Trim();
        if (host.Contains("://"))
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return null;
            host = uri.Host;
        }

        host = host.TrimEnd('.').ToLowerInvariant();
        try
        {
            host = s_Idn.GetAscii(host);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        return host.Length <= MaxLength && DomainPattern().IsMatch(host) ? host : null;
    }

    /// <summary>True when <paramref name="domain"/> is one of <paramref name="excluded"/> or a subdomain of one.</summary>
    public static bool IsExcluded(string domain, IEnumerable<string> excluded) =>
        excluded.Any(e => domain == e || domain.EndsWith("." + e, StringComparison.Ordinal));

    // Labels of letters, digits and hyphens (not at either end), separated by dots. Covers IPv4 addresses too.
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex DomainPattern();
}
