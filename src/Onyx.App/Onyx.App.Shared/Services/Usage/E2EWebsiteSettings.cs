using Onyx.App.Shared.Services.Client;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>
/// Excluded websites live in the encrypted settings; the desktop collectors read them from there. Removing
/// websites from history means rewriting the encrypted rows here, since the server can't read them.
/// </summary>
public sealed class E2EWebsiteSettings(OnyxApiClient api, VaultService vault, E2EUsageDataService usage)
    : IWebsiteSettings
{
    /// <summary>How far back history is rewritten.</summary>
    private static readonly TimeSpan s_History = TimeSpan.FromDays(3 * 365);

    public async Task<List<string>> GetExcludedDomainsAsync() =>
        (await vault.GetSettingsAsync()).ExcludedDomains.Order().ToList();

    public async Task<List<string>> SetExcludedDomainsAsync(IReadOnlyList<string> domains)
    {
        var normalized = new List<string>();
        foreach (var domain in domains)
        {
            normalized.Add(WebDomain.Normalize(domain)
                           ?? throw new ArgumentException($"\"{domain}\" is not a valid website, e.g. example.com"));
        }

        normalized = normalized.Distinct().Order().ToList();
        var previous = (await vault.GetSettingsAsync()).ExcludedDomains.ToHashSet();
        await vault.UpdateSettingsAsync(s => s.ExcludedDomains = normalized);

        var added = normalized.Where(d => !previous.Contains(d)).ToList();
        if (added.Count > 0)
            await RewriteAsync(domain => WebDomain.IsExcluded(domain, added), null);
        return normalized;
    }

    public Task DeleteWebsiteHistoryAsync(Action<string>? progress = null) => RewriteAsync(_ => true, progress);

    /// <summary>Moves the time of matching websites to the browser's time without a website, on all devices.</summary>
    private async Task RewriteAsync(Func<string, bool> matches, Action<string>? progress)
    {
        var v = await vault.GetVaultAsync();
        var end = DateTime.UtcNow.AddHours(2);
        var start = end - s_History;
        progress?.Invoke("Loading history…");
        var rows = await usage.GetDecryptedAsync(start, end);

        var changed = rows
            .Where(r => r.Payload.Parts.Any(p => p.Domain is { } d && matches(d)))
            .Select(r => r with
            {
                Payload = r.Payload with
                {
                    Parts = r.Payload.Parts
                        .GroupBy(p => p.Domain is { } d && matches(d) ? null : p.Domain)
                        // The browser keeps its own opens; visits of the removed websites are dropped.
                        .Select(g => new UsagePart(g.Key, g.Sum(p => p.Seconds), g.Where(p => p.Domain == g.Key).Sum(p => p.Opens)))
                        .ToList()
                }
            })
            .ToList();

        foreach (var device in changed.GroupBy(r => r.DeviceId))
        {
            progress?.Invoke($"Rewriting {device.Count()} hours…");
            await api.PutEncryptedUsageAsync(device.Key, await v.EncryptRowsAsync(device.Key, device.ToList()));
        }
    }
}
