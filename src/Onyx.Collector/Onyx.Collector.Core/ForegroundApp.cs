namespace Onyx.Collector.Core;

/// <summary>The app that currently has the user's attention.</summary>
/// <param name="Identifier">Platform-qualified stable key, e.g. <c>windows:chrome.exe</c> or <c>linux:firefox</c>.
/// The server creates one app per identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Category">Optional category.</param>
public record ForegroundApp(string Identifier, string Name, string? Category = null);

/// <summary>Reports which app is in the foreground right now (desktop platforms, polled).</summary>
public interface IForegroundAppSource
{
    /// <returns>Null when no app has focus (desktop shown, screen locked, unsupported session, ...).</returns>
    ForegroundApp? GetForegroundApp();
}

/// <summary>Reports how long the user has not touched keyboard or mouse.</summary>
public interface IIdleTimeSource
{
    /// <returns>Null when the platform can't tell.</returns>
    TimeSpan? GetIdleTime();
}

/// <summary>Supplies PNG icons for apps the server has no icon for yet.</summary>
public interface IAppIconSource
{
    byte[]? GetIconPng(string appIdentifier);
}
