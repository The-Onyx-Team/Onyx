namespace Onyx.App.Shared.Services;

/// <summary>
/// The Onyx server the app talks to. The native apps let the user pick it, so they work with any self-hosted
/// server; the web app is the server and can't be pointed elsewhere.
/// </summary>
public interface IServerSettings
{
    /// <summary>Whether the user can change the server (native apps only).</summary>
    bool IsConfigurable { get; }

    /// <summary>The server's base address, e.g. <c>https://onyx.example.com</c>.</summary>
    string ServerUrl { get; }

    /// <summary>
    /// Checks that an Onyx server answers at <paramref name="url"/> and saves it. Only call while signed out:
    /// tokens and the device id belong to the previous server.
    /// </summary>
    /// <returns>Null on success, otherwise a message for the user.</returns>
    Task<string?> SetServerUrlAsync(string url);

    /// <summary>Public information of the current server; null if it can't be reached.</summary>
    Task<Onyx.Data.ApiSchema.ServerInfoDto?> GetInfoAsync();
}
