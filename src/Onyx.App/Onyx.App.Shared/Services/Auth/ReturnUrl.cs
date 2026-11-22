namespace Onyx.App.Shared.Services.Auth;

/// <summary>Where to go after signing in. Only paths on this site are allowed (no open redirects).</summary>
public static class ReturnUrl
{
    public const string Default = "/home";

    public static string Sanitize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Default;

        url = url.Trim();
        // "/x" is local; "//host" and "/\host" are treated as other hosts by browsers.
        var local = url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
        if (!local || url == "/" || url.StartsWith("/account/login", StringComparison.OrdinalIgnoreCase))
            return Default;

        return url;
    }
}
