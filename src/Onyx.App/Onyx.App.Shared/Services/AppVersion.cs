using System.Reflection;

namespace Onyx.App.Shared.Services;

/// <summary>
/// A build's version, from its assembly: the number from Directory.Build.props (or the release tag) and the git
/// commit .NET appends to the informational version ("0.0+3f2a1b9c…").
/// </summary>
public sealed record AppVersion(string Number, string? Commit)
{
    /// <summary>This client (the shared UI is built with the app or the web client).</summary>
    public static AppVersion Current { get; } = Of(typeof(AppVersion).Assembly);

    /// <summary>Changes with every build of the shared UI; appended to its CSS and JS addresses so browsers and
    /// the apps' web view never keep an old copy after an update.</summary>
    public static string CacheToken { get; } = typeof(AppVersion).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];

    /// <summary>"0.0" or "0.0 (3f2a1b9)".</summary>
    public string Full => Commit is null ? Number : $"{Number} ({Commit})";

    public static AppVersion Of(Assembly assembly) =>
        Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
              ?? assembly.GetName().Version?.ToString(2) ?? "unknown");

    /// <summary>Reads "0.0+3f2a1b9c…" or "0.0 (3f2a1b9)".</summary>
    public static AppVersion Parse(string text)
    {
        text = text.Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            var commit = text[(plus + 1)..];
            return new AppVersion(text[..plus], commit.Length > 7 ? commit[..7] : commit);
        }

        var paren = text.IndexOf(" (", StringComparison.Ordinal);
        return paren >= 0 ? new AppVersion(text[..paren], text[(paren + 2)..].TrimEnd(')')) : new AppVersion(text, null);
    }
}
