namespace Onyx.App.Shared.Services.Usage;

/// <summary>The signed-in user's website recording settings (shared by all their devices, encrypted).</summary>
public interface IWebsiteSettings
{
    /// <summary>Websites never recorded, normalized and sorted.</summary>
    Task<List<string>> GetExcludedDomainsAsync();

    /// <summary>Replaces the exclusion list. Time already recorded for newly excluded websites (and their
    /// subdomains) keeps counting for the browser, but loses the domain.</summary>
    /// <returns>The stored, normalized list.</returns>
    /// <exception cref="ArgumentException">An entry is not a valid domain.</exception>
    Task<List<string>> SetExcludedDomainsAsync(IReadOnlyList<string> domains);

    /// <summary>Removes the website from all recorded usage. Browser time is kept.</summary>
    /// <param name="progress">Called with a short status while rows are rewritten.</param>
    Task DeleteWebsiteHistoryAsync(Action<string>? progress = null);
}
