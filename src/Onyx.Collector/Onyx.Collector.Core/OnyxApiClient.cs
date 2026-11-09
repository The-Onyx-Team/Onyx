using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core;

/// <summary>What a collector needs to remember between runs.</summary>
public sealed record CollectorCredentials(string AccessToken, string RefreshToken, int? DeviceId = null);

/// <summary>Where a collector keeps its tokens and device id (secure storage, a 0600 file, ...).</summary>
public interface ICredentialStore
{
    Task<CollectorCredentials?> LoadAsync();
    Task SaveAsync(CollectorCredentials credentials);
    Task ClearAsync();
}

/// <summary>The user has to sign in (again): no tokens, or the refresh token was rejected.</summary>
public sealed class NotSignedInException(string message) : Exception(message);

/// <summary>
/// Authenticated client for the Onyx API. Refreshes the access token before it expires or after a 401, stores
/// rotated tokens, and registers the device again if it was deleted on the server.
/// </summary>
public sealed class OnyxApiClient(HttpClient http, ICredentialStore store, ILogger? logger = null)
{
    /// <summary>Same limit as the server; larger snapshots are sent in several requests.</summary>
    public const int MaxEntriesPerUpload = 5000;

    private static readonly TimeSpan s_RefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);

    private readonly ILogger m_Logger = logger ?? NullLogger.Instance;
    private readonly SemaphoreSlim m_RefreshLock = new(1, 1);

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/api/auth/login", new LoginDto(email, password), s_Json, ct);
        if (!response.IsSuccessStatusCode)
            throw new NotSignedInException("Invalid email or password");

        var tokens = await response.Content.ReadFromJsonAsync<LoginResultDto>(s_Json, ct)
                     ?? throw new InvalidOperationException("Empty login response");

        // A new login may belong to another account, so the old device id is not reused.
        await store.SaveAsync(new CollectorCredentials(tokens.Token, tokens.RefreshToken));
    }

    public Task LogoutAsync() => store.ClearAsync();

    public async Task<bool> IsSignedInAsync() => await store.LoadAsync() is not null;

    /// <summary>Returns the stored device id, registering this device first if there is none.</summary>
    public async Task<int> EnsureDeviceAsync(string name, string platform, CancellationToken ct = default)
    {
        var credentials = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
        if (credentials.DeviceId is { } id)
            return id;

        var device = await SendAsync<DeviceDto>(HttpMethod.Post, "/api/devices", new CreateDeviceDto(name, platform), ct);
        credentials = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
        await store.SaveAsync(credentials with { DeviceId = device.Id });
        m_Logger.LogInformation("Registered device {Name} ({Platform}) as {Id}", name, platform, device.Id);
        return device.Id;
    }

    /// <summary>Uploads usage buckets for this device, in chunks.</summary>
    /// <returns>Identifiers of uploaded apps the server has no icon for.</returns>
    public async Task<HashSet<string>> UploadUsageAsync(IReadOnlyList<UsageEntryDto> entries, string deviceName,
        string platform, CancellationToken ct = default)
    {
        var missingIcons = new HashSet<string>();

        foreach (var chunk in entries.Chunk(MaxEntriesPerUpload))
        {
            var deviceId = await EnsureDeviceAsync(deviceName, platform, ct);
            UsageUploadResultDto result;
            try
            {
                result = await SendAsync<UsageUploadResultDto>(HttpMethod.Post, $"/api/devices/{deviceId}/usage",
                    new UsageUploadDto(chunk.ToList()), ct);
            }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // The device was removed in the dashboard. Register again and retry once.
                m_Logger.LogInformation("Device {Id} no longer exists on the server, registering again", deviceId);
                var credentials = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
                await store.SaveAsync(credentials with { DeviceId = null });
                deviceId = await EnsureDeviceAsync(deviceName, platform, ct);
                result = await SendAsync<UsageUploadResultDto>(HttpMethod.Post, $"/api/devices/{deviceId}/usage",
                    new UsageUploadDto(chunk.ToList()), ct);
            }

            missingIcons.UnionWith(result.AppsMissingIcon);
        }

        return missingIcons;
    }

    public async Task UploadIconAsync(string appIdentifier, byte[] png, CancellationToken ct = default)
    {
        try
        {
            await SendAsync<object?>(HttpMethod.Put, "/api/app/icon", new AppIconDto(appIdentifier, png), ct);
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            // Someone else uploaded it first, or the image was rejected; neither is worth retrying.
            m_Logger.LogDebug("Icon for {App} not stored: {Status}", appIdentifier, e.StatusCode);
        }
    }

    public Task<List<DeviceDto>> GetDevicesAsync(CancellationToken ct = default) =>
        SendAsync<List<DeviceDto>>(HttpMethod.Get, "/api/devices", null, ct);

    public Task<DeviceDto> RegisterDeviceAsync(string name, string? platform, CancellationToken ct = default) =>
        SendAsync<DeviceDto>(HttpMethod.Post, "/api/devices", new CreateDeviceDto(name, platform), ct);

    public Task DeleteDeviceAsync(int deviceId, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Delete, $"/api/devices/{deviceId}", null, ct);

    public Task<List<UsageDto>> GetUsageAsync(DateTime start, DateTime end, int? deviceId, CancellationToken ct = default)
    {
        var query = $"/api/data/usage?startTime={Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))}" +
                    $"&endTime={Uri.EscapeDataString(end.ToUniversalTime().ToString("O"))}" +
                    (deviceId is null ? "" : $"&deviceId={deviceId}");
        return SendAsync<List<UsageDto>>(HttpMethod.Get, query, null, ct);
    }

    /// <summary>Deletes the signed-in account on the server, then forgets the local credentials.</summary>
    public async Task DeleteAccountAsync(CancellationToken ct = default)
    {
        await SendAsync<object?>(HttpMethod.Delete, "/api/user/account", null, ct);
        await store.ClearAsync();
    }

    public Task<List<AppDto>> GetAppsAsync(CancellationToken ct = default) =>
        SendAsync<List<AppDto>>(HttpMethod.Get, "/api/app", null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(forceRefresh: false, ct);
        using var response = await SendOnceAsync(method, path, body, token, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            token = await GetAccessTokenAsync(forceRefresh: true, ct);
            using var retry = await SendOnceAsync(method, path, body, token, ct);
            return await ReadAsync<T>(retry, ct);
        }

        return await ReadAsync<T>(response, ct);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string token,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: s_Json);
        return await http.SendAsync(request, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new NotSignedInException("The server rejected the access token");

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}", null,
                response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;

        return (await response.Content.ReadFromJsonAsync<T>(s_Json, ct))!;
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        var credentials = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
        if (!forceRefresh && !ExpiresSoon(credentials.AccessToken))
            return credentials.AccessToken;

        await m_RefreshLock.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while this one waited.
            var current = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
            if (current.AccessToken != credentials.AccessToken && !ExpiresSoon(current.AccessToken))
                return current.AccessToken;

            using var response = await http.PostAsJsonAsync("/api/auth/refresh",
                new RefreshTokenDto(current.RefreshToken), s_Json, ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            {
                await store.ClearAsync();
                throw new NotSignedInException("The session expired, please sign in again");
            }

            response.EnsureSuccessStatusCode();
            var tokens = await response.Content.ReadFromJsonAsync<LoginResultDto>(s_Json, ct)
                         ?? throw new InvalidOperationException("Empty refresh response");

            await store.SaveAsync(current with { AccessToken = tokens.Token, RefreshToken = tokens.RefreshToken });
            return tokens.Token;
        }
        finally
        {
            m_RefreshLock.Release();
        }
    }

    /// <summary>Reads the <c>exp</c> claim without validating; the server does the validation.</summary>
    internal static bool ExpiresSoon(string jwt)
    {
        try
        {
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var exp = DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("exp").GetInt64());
            return exp - DateTimeOffset.UtcNow < s_RefreshMargin;
        }
        catch (Exception e) when (e is FormatException or JsonException or KeyNotFoundException or IndexOutOfRangeException
                                      or InvalidOperationException)
        {
            return true;
        }
    }
}
