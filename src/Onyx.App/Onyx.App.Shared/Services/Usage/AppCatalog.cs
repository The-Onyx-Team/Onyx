using Onyx.App.Shared.Services.Client;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;

namespace Onyx.App.Shared.Services.Usage;

/// <summary>One app identifier on one platform, with what the devices reported and the user's own settings.</summary>
public sealed record AppEntry(
    string Identifier,
    string Tag,
    string ReportedName,
    string? ReportedCategory,
    byte[]? ReportedIcon,
    byte[]? CustomIcon,
    AppSettings? Settings)
{
    public string Name => string.IsNullOrWhiteSpace(Settings?.Name) ? ReportedName : Settings!.Name!.Trim();

    public string? Category => string.IsNullOrWhiteSpace(Settings?.Category)
        ? AppCatalog.MapReportedCategory(ReportedCategory)
        : Settings!.Category!.Trim();

    public bool Hidden => Settings?.Hidden == true;

    public byte[]? Icon => CustomIcon ?? ReportedIcon;

    /// <summary>"Android", "Windows", "Linux", from the identifier's prefix.</summary>
    public string Platform => Identifier.Split(':', 2)[0] switch
    {
        "android" => "Android",
        "windows" => "Windows",
        "linux" => "Linux",
        var other => other
    };
}

/// <summary>
/// Apps shown as one: all identifiers with the same name (ignoring case). The same app on several platforms
/// ("Firefox" on Android and Linux) is one app on the dashboard; renaming one of them splits it off again.
/// </summary>
public sealed class AppGroup
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<AppEntry> Members { get; init; }

    public string? Category => Members.Select(m => m.Category).FirstOrDefault(c => c is not null);
    public bool Hidden => Members.All(m => m.Hidden);
    public byte[]? Icon => Members.Select(m => m.CustomIcon).FirstOrDefault(i => i is not null)
                           ?? Members.Select(m => m.ReportedIcon).FirstOrDefault(i => i is not null);
    public bool HasCustomIcon => Members.Any(m => m.CustomIcon is not null);
    public bool IsCustomized => Members.Any(m => m.Settings is { IsEmpty: false } || m.CustomIcon is not null);

    public string? IconDataUrl => Icon is { } icon ? AppCatalog.DataUrl(icon) : null;

    public string Platforms => string.Join(", ", Members.Select(m => m.Platform).Distinct());
}

/// <summary>
/// The user's apps: decrypted app information combined with their own names, categories and icons, and grouped
/// by name. Loaded once and kept until something changes.
/// </summary>
public sealed class AppCatalog(OnyxApiClient api, VaultService vault)
{
    private Dictionary<string, AppEntry>? m_ByTag;
    private List<AppGroup>? m_Groups;
    private Dictionary<string, AppGroup>? m_GroupByTag;

    public async Task<IReadOnlyList<AppGroup>> GetGroupsAsync(bool refresh = false)
    {
        await LoadAsync(refresh);
        return m_Groups!;
    }

    /// <summary>The group an app (by tag) belongs to; null for apps without information yet.</summary>
    public async Task<AppGroup?> GroupOfTagAsync(string tag)
    {
        await LoadAsync(false);
        return m_GroupByTag!.GetValueOrDefault(tag);
    }

    public async Task<IReadOnlyDictionary<string, AppGroup>> GroupsByTagAsync(bool refresh = false)
    {
        await LoadAsync(refresh);
        return m_GroupByTag!;
    }

    public void Invalidate()
    {
        m_ByTag = null;
        m_Groups = null;
        m_GroupByTag = null;
    }

    /// <summary>Sets the user's own name, category and hidden flag of apps, in one settings change. Empty
    /// settings remove the user's changes.</summary>
    public async Task SetSettingsAsync(IReadOnlyDictionary<string, AppSettings> apps)
    {
        await vault.UpdateSettingsAsync(settings =>
        {
            foreach (var (id, app) in apps)
            {
                if (app.IsEmpty)
                    settings.Apps.Remove(id);
                else
                    settings.Apps[id] = app;
            }
        });
        Invalidate();
    }

    /// <summary>Sets or removes (null) the user's own icon for apps.</summary>
    public async Task SetCustomIconAsync(IEnumerable<string> identifiers, byte[]? png)
    {
        await LoadAsync(false);
        var v = await vault.GetVaultAsync();
        var updated = new List<Onyx.Data.ApiSchema.EncryptedAppDto>();
        foreach (var id in identifiers)
        {
            var entry = m_ByTag!.Values.FirstOrDefault(e => e.Identifier == id);
            if (entry is null)
                continue;
            updated.Add(await v.EncryptAppAsync(new AppInfoPayload(entry.Identifier, entry.ReportedName,
                entry.ReportedCategory, entry.ReportedIcon, png)));
        }

        await api.PutEncryptedAppsAsync(updated, createOnly: false);
        Invalidate();
    }

    private async Task LoadAsync(bool refresh)
    {
        if (m_Groups is not null && !refresh)
            return;

        var v = await vault.GetVaultAsync();
        var settings = await vault.GetSettingsAsync(refresh);
        var apps = await v.DecryptAppsAsync(await api.GetEncryptedAppsAsync());

        m_ByTag = apps.ToDictionary(a => a.Key, a => new AppEntry(a.Value.Identifier, a.Key, a.Value.Name,
            a.Value.Category, a.Value.Icon, a.Value.CustomIcon, settings.Apps.GetValueOrDefault(a.Value.Identifier)));

        m_Groups = Group(m_ByTag.Values);
        m_GroupByTag = m_Groups.SelectMany(g => g.Members.Select(m => (m.Tag, g))).ToDictionary(x => x.Tag, x => x.g);
    }

    /// <summary>Groups apps by their shown name, ignoring case and surrounding spaces.</summary>
    public static List<AppGroup> Group(IEnumerable<AppEntry> entries)
    {
        var id = 1;
        return entries
            .GroupBy(e => e.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AppGroup { Id = id++, Name = g.First().Name, Members = g.OrderBy(e => e.Platform).ToList() })
            .ToList();
    }

    /// <summary>Platform categories (Android's, freedesktop's) mapped to the default categories.</summary>
    public static string? MapReportedCategory(string? reported) => VaultSettings.MapReportedCategory(reported);

    public static string DataUrl(byte[] image)
    {
        var type = image.AsSpan().StartsWith("<svg"u8) || image.AsSpan().StartsWith("<?xml"u8) ? "image/svg+xml"
            : image.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }) ? "image/jpeg"
            : "image/png";
        return $"data:{type};base64,{Convert.ToBase64String(image)}";
    }
}
