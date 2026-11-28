using System.Net;
using System.Text.Json;
using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Integrations;

/// <summary>An integration with what the server knows about it.</summary>
public sealed record IntegrationState(IntegrationSettings Settings, IntegrationDto? Server)
{
    public string KeyText => Settings.Key.ToString();
}

/// <summary>
/// Integrations: other programs that read the user's numbers with a key of their own (see
/// <see cref="IntegrationKey"/>). This device keeps their snapshots up to date, encrypted for each key.
/// </summary>
public sealed class IntegrationService(OnyxApiClient api, VaultService vault, IUsageDataService usage, TimeProvider time)
{
    /// <summary>Snapshots are published again when the dashboard opens and they are older than this.</summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<List<IntegrationState>> GetAsync()
    {
        var settings = await vault.GetSettingsAsync();
        var server = (await api.SendAuthorizedAsync<List<IntegrationDto>>(HttpMethod.Get, "/api/integrations"))
            .ToDictionary(i => i.Id);
        return settings.Integrations.Select(i => new IntegrationState(i, server.GetValueOrDefault(i.Id))).ToList();
    }

    /// <summary>Creates a key, registers it, keeps it in the encrypted settings and publishes the first snapshot.</summary>
    public async Task<IntegrationSettings> CreateAsync(IntegrationSettings integration, TimeZoneInfo zone)
    {
        var key = IntegrationKey.Create();
        integration.Id = key.Id;
        integration.Secret = IntegrationKey.Base64Url(key.Secret);
        integration.Created = time.GetUtcNow();

        await api.SendAuthorizedAsync<object?>(HttpMethod.Post, "/api/integrations",
            new CreateIntegrationDto(key.Id, await key.AccessTokenAsync(api.Crypto)));
        try
        {
            await vault.UpdateSettingsAsync(s => s.Integrations.Add(integration));
        }
        catch
        {
            await DeleteOnServerAsync(key.Id);
            throw;
        }

        await PublishAsync([integration], zone);
        return integration;
    }

    /// <summary>Saves a changed name or scope and publishes a snapshot with the new scope at once.</summary>
    public async Task UpdateAsync(IntegrationSettings changed, TimeZoneInfo zone)
    {
        await vault.UpdateSettingsAsync(s =>
        {
            var index = s.Integrations.FindIndex(i => i.Id == changed.Id);
            if (index >= 0)
                s.Integrations[index] = changed;
        });
        await PublishAsync([changed], zone);
    }

    /// <summary>Removes the integration: its key stops working immediately.</summary>
    public async Task DeleteAsync(string id)
    {
        await DeleteOnServerAsync(id);
        await vault.UpdateSettingsAsync(s => s.Integrations.RemoveAll(i => i.Id == id));
    }

    /// <summary>Publishes snapshots older than <see cref="PublishInterval"/>. Called when the dashboard opens.</summary>
    /// <returns>Number of snapshots published.</returns>
    public async Task<int> PublishDueAsync(TimeZoneInfo zone)
    {
        var settings = await vault.GetSettingsAsync();
        if (settings.Integrations.Count == 0)
            return 0;

        var now = time.GetUtcNow().UtcDateTime;
        var states = await GetAsync();
        var due = states
            .Where(s => s.Server is not null && (s.Server.DataUpdatedAt is not { } at || now - at >= PublishInterval))
            .Select(s => s.Settings)
            .ToList();
        await PublishAsync(due, zone);
        return due.Count;
    }

    /// <summary>Builds and uploads the snapshots, loading the usage once for all of them.</summary>
    public async Task PublishAsync(IReadOnlyList<IntegrationSettings> integrations, TimeZoneInfo zone)
    {
        if (integrations.Count == 0)
            return;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var from = integrations.Min(i => IntegrationSnapshotBuilder.Range(i, today).From);
        var (rows, unlocks) = await usage.GetUsageAndUnlocksAsync(
            TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone).AddHours(-1),
            TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue), zone).AddHours(1));

        foreach (var integration in integrations)
        {
            var snapshot = IntegrationSnapshotBuilder.Build(integration, rows, unlocks, zone, today, now);
            var key = integration.Key;
            var blob = await api.Crypto.EncryptAsync(await key.EncryptionKeyAsync(api.Crypto),
                JsonSerializer.SerializeToUtf8Bytes(snapshot, s_Json), key.Context);
            await api.SendAuthorizedAsync<object?>(HttpMethod.Put, $"/api/integrations/{key.Id}/data",
                new IntegrationDataDto(blob));
        }
    }

    private async Task DeleteOnServerAsync(string id)
    {
        try
        {
            await api.SendAuthorizedAsync<object?>(HttpMethod.Delete, $"/api/integrations/{id}");
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone (removed on another device).
        }
    }

    /// <summary>Decrypts a snapshot the way an integration does (for tests and the "Preview" in settings).</summary>
    public static async Task<IntegrationSnapshot?> DecryptAsync(IOnyxCrypto crypto, IntegrationKey key, byte[] blob) =>
        JsonSerializer.Deserialize<IntegrationSnapshot>(
            await crypto.DecryptAsync(await key.EncryptionKeyAsync(crypto), blob, key.Context), s_Json);

    public static JsonSerializerOptions JsonOptions => s_Json;
}
