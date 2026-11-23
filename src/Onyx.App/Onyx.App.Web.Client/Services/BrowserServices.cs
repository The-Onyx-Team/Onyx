using System.Net.Http.Json;
using System.Text.Json;
using Onyx.App.Shared.Services;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Client.Services;

/// <summary>
/// Tokens in the browser's local storage, so the web app stays signed in. The encryption key is kept in session
/// storage (gone when the browser closes) unless the user chose to stay unlocked.
/// </summary>
public sealed class BrowserCredentialStore(OnyxJs js) : ICredentialStore, IVaultKeyOptions
{
    private const string TokensKey = "onyx.tokens";
    private const string VaultKeyKey = "onyx.vaultKey";
    private const string RememberKey = "onyx.rememberKey";

    private bool? m_Remember;

    public bool CanChoose => true;

    public bool Remember
    {
        get => m_Remember ?? false;
        set => m_Remember = value;
    }

    public async Task<CollectorCredentials?> LoadAsync()
    {
        m_Remember ??= await js.InvokeAsync<string?>("storageGet", "local", RememberKey) == "true";
        if (await js.InvokeAsync<string?>("storageGet", "local", TokensKey) is not { } json)
            return null;

        try
        {
            var tokens = JsonSerializer.Deserialize<Tokens>(json);
            if (tokens is null)
                return null;
            var key = await js.InvokeAsync<string?>("storageGet", "session", VaultKeyKey)
                      ?? await js.InvokeAsync<string?>("storageGet", "local", VaultKeyKey);
            return new CollectorCredentials(tokens.AccessToken, tokens.RefreshToken, null, key);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(CollectorCredentials credentials)
    {
        await js.InvokeVoidAsync("storageSet", "local", TokensKey,
            JsonSerializer.Serialize(new Tokens(credentials.AccessToken, credentials.RefreshToken)));

        await js.InvokeVoidAsync("storageRemove", "session", VaultKeyKey);
        await js.InvokeVoidAsync("storageRemove", "local", VaultKeyKey);
        if (credentials.VaultKey is { } key)
            await js.InvokeVoidAsync("storageSet", Remember ? "local" : "session", VaultKeyKey, key);
        await js.InvokeVoidAsync("storageSet", "local", RememberKey, Remember ? "true" : "false");
    }

    public async Task ClearAsync()
    {
        await js.InvokeVoidAsync("storageRemove", "local", TokensKey);
        await js.InvokeVoidAsync("storageRemove", "session", VaultKeyKey);
        await js.InvokeVoidAsync("storageRemove", "local", VaultKeyKey);
    }

    private sealed record Tokens(string AccessToken, string RefreshToken);
}

public sealed class BrowserStorage(OnyxJs js) : IStorage
{
    public async ValueTask<bool> ContainKeyAsync(string key) =>
        await js.InvokeAsync<string?>("storageGet", "local", key) is not null;

    public async ValueTask<T?> GetItemAsync<T>(string key) =>
        await js.InvokeAsync<string?>("storageGet", "local", key) is { } json ? JsonSerializer.Deserialize<T>(json) : default;

    public async ValueTask SetItemAsync<T>(string key, T value) =>
        await js.InvokeVoidAsync("storageSet", "local", key, JsonSerializer.Serialize(value));
}

public sealed class BrowserPlatformService : IPlatformService
{
    public bool IsMaui() => false;
}

/// <summary>The web app is served by the server it talks to.</summary>
public sealed class BrowserServerSettings(HttpClient http) : IServerSettings
{
    private ServerInfoDto? m_Info;

    public bool IsConfigurable => false;

    public string ServerUrl => http.BaseAddress!.GetLeftPart(UriPartial.Authority);

    public Task<string?> SetServerUrlAsync(string url) =>
        throw new NotSupportedException("The web app can't change its server.");

    public async Task<ServerInfoDto?> GetInfoAsync()
    {
        try
        {
            return m_Info ??= await http.GetFromJsonAsync<ServerInfoDto>("/api/info");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}

/// <summary>The browser does not collect usage.</summary>
public sealed class BrowserStatsService : IStatsService
{
    private static readonly CollectionStatus s_Status = new(false, false, null, null, 0);
    public CollectionStatus GetStatus() => s_Status;
    public void OpenUsageAccessSettings() { }
    public Task<CollectionStatus> SyncNowAsync() => Task.FromResult(s_Status);
}

public sealed class BrowserFileSaver(OnyxJs js) : IFileSaver
{
    public async Task SaveAsync(string fileName, string contentType, byte[] data) =>
        await js.InvokeVoidAsync("download", fileName, contentType, data);
}
