using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

public interface IAppManager
{
    /// <summary>Apps the signed-in user has usage data for, with icons.</summary>
    public Task<List<AppDto>> GetAppsAsync();

    /// <summary>Stores an icon for an app that has none yet. Icons are shared between users, so an existing
    /// icon is never replaced.</summary>
    /// <returns>False if the app is unknown or already has an icon.</returns>
    /// <exception cref="ArgumentException">The data is not a PNG or too large.</exception>
    public Task<bool> SetIconAsync(string appIdentifier, byte[] iconPng);

    /// <summary>
    /// Sets the user's own name and category for an app (only for this user; apps are shared). Empty values keep
    /// the reported ones; both empty removes the override.
    /// </summary>
    /// <returns>False if the user has no data for the app.</returns>
    /// <exception cref="ArgumentException">A value is too long.</exception>
    public Task<bool> SetOverrideAsync(int appId, string? displayName, string? category);
}
