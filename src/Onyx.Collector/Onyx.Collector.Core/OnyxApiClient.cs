using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core;

/// <summary>What a collector needs to remember between runs.</summary>
/// <param name="VaultKey">The user's master key (base64) once the device is unlocked; uploads are encrypted with
/// it. Null until the user enters their passphrase on this device.</param>
public sealed record CollectorCredentials(string AccessToken, string RefreshToken, int? DeviceId = null,
    string? VaultKey = null);

/// <summary>Where a collector keeps its tokens and device id (secure storage, a 0600 file, ...).</summary>
public interface ICredentialStore
{
    Task<CollectorCredentials?> LoadAsync();
    Task SaveAsync(CollectorCredentials credentials);

    /// <summary>Forgets tokens and the encryption key (signing out).</summary>
    Task ClearAsync();

    /// <summary>The device id this installation registered under, even while signed out, so signing in to the
    /// same account again doesn't add a second device. Null where devices are not registered.</summary>
    Task<int?> LoadDeviceIdAsync() => Task.FromResult<int?>(null);
}

/// <summary>The user has to sign in (again): no tokens, or the refresh token was rejected.</summary>
public class NotSignedInException(string message) : Exception(message);

/// <summary>Signed in, but the encryption key is not on this device yet: the user has to enter their passphrase
/// (or create one, if the account has no encryption set up yet).</summary>
public sealed class VaultLockedException(string message) : NotSignedInException(message);

/// <summary>The password was right, but the account also needs a code from the authenticator app.</summary>
public sealed class TwoFactorRequiredException()
    : NotSignedInException("Enter the code from your authenticator app.");

/// <summary>The settings changed on another device since they were loaded.</summary>
public sealed class VaultConflictException() : Exception("The settings were changed on another device");

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

    /// <param name="twoFactorCode">For accounts with two-factor authentication, after
    /// <see cref="TwoFactorRequiredException"/>.</param>
    public async Task LoginAsync(string email, string password, CancellationToken ct = default,
        string? twoFactorCode = null)
    {
        using var response = await http.PostAsJsonAsync("/api/auth/login",
            new LoginDto(email, password, twoFactorCode), s_Json, ct);
        if ((int)response.StatusCode == TwoFactor.RequiredStatus)
            throw new TwoFactorRequiredException();
        if (!response.IsSuccessStatusCode)
        {
            throw new NotSignedInException(twoFactorCode is null
                ? "Invalid email or password"
                : ErrorMessage(new HttpRequestException($"{(int)response.StatusCode}: " +
                                                        await response.Content.ReadAsStringAsync(ct))));
        }

        var tokens = await response.Content.ReadFromJsonAsync<LoginResultDto>(s_Json, ct)
                     ?? throw new InvalidOperationException("Empty login response");

        // A new login may belong to another account, so the old device id is not reused.
        await store.SaveAsync(new CollectorCredentials(tokens.Token, tokens.RefreshToken));
    }

    public async Task LogoutAsync()
    {
        m_Vault = null;
        await store.ClearAsync();
    }

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

    /// <summary>
    /// Encrypts usage buckets for this device and uploads them, in chunks. Then sends name, category and icon of
    /// apps the server has no information for yet (encrypted as well).
    /// </summary>
    /// <param name="icons">Icons of apps, or null if the platform has none.</param>
    /// <returns>Number of rows stored.</returns>
    /// <exception cref="VaultLockedException">The encryption key is not on this device.</exception>
    public async Task<int> UploadUsageAsync(IReadOnlyList<UsageEntryDto> entries, string deviceName,
        string platform, IAppIconSource? icons = null, CancellationToken ct = default)
    {
        var vault = await GetVaultAsync() ?? throw new VaultLockedException(LockedMessage);
        var stored = 0;
        var missing = new Dictionary<string, string>();

        foreach (var chunk in ChunkByHour(entries))
        {
            var deviceId = await EnsureDeviceAsync(deviceName, platform, ct);
            EncryptedUsageUploadResultDto result;
            Dictionary<string, string> identifiers;
            try
            {
                (result, identifiers) = await UploadEncryptedAsync(vault, deviceId, chunk, ct);
            }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // The device was removed in the dashboard. Register again and retry once. The device id is part of
                // what is encrypted, so the chunk is encrypted again.
                m_Logger.LogInformation("Device {Id} no longer exists on the server, registering again", deviceId);
                var credentials = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
                await store.SaveAsync(credentials with { DeviceId = null });
                deviceId = await EnsureDeviceAsync(deviceName, platform, ct);
                (result, identifiers) = await UploadEncryptedAsync(vault, deviceId, chunk, ct);
            }

            stored += result.Stored;
            foreach (var tag in result.TagsMissingInfo)
                if (identifiers.TryGetValue(tag, out var identifier))
                    missing[tag] = identifier;
        }

        if (missing.Count > 0)
            await UploadAppInfoAsync(vault, missing.Values, entries, icons, ct);

        return stored;
    }

    private async Task<(EncryptedUsageUploadResultDto, Dictionary<string, string>)> UploadEncryptedAsync(
        UserVault vault, int deviceId, UsageEntryDto[] chunk, CancellationToken ct)
    {
        var (rows, identifiers) = await vault.EncryptUsageAsync(deviceId, chunk);
        var result = await SendAsync<EncryptedUsageUploadResultDto>(HttpMethod.Post,
            $"/api/devices/{deviceId}/e2e-usage", new EncryptedUsageUploadDto(rows), ct);
        return (result, identifiers);
    }

    /// <summary>Sends name, category and icon of apps, unless the server already has them (another device of the
    /// user may have sent them, and the user's own icon must not be overwritten).</summary>
    private async Task UploadAppInfoAsync(UserVault vault, IEnumerable<string> identifiers,
        IReadOnlyList<UsageEntryDto> entries, IAppIconSource? icons, CancellationToken ct)
    {
        var apps = new List<EncryptedAppDto>();
        foreach (var identifier in identifiers)
        {
            var entry = entries.Last(e => e.AppIdentifier == identifier);
            byte[]? icon = null;
            try
            {
                icon = icons?.GetIconPng(identifier) is { Length: > 0 and <= MaxIconBytes } png ? png : null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                m_Logger.LogDebug(e, "No icon for {App}", identifier);
            }

            apps.Add(await vault.EncryptAppAsync(new AppInfoPayload(identifier, entry.AppName, entry.Category, icon)));
        }

        try
        {
            await PutEncryptedAppsAsync(apps, createOnly: true, ct);
        }
        catch (HttpRequestException e)
        {
            // Not worth failing the upload for: the server asks again next time.
            m_Logger.LogDebug("App information not stored: {Status}", e.StatusCode);
        }
    }

    /// <summary>Icons larger than this are not sent.</summary>
    public const int MaxIconBytes = 64 * 1024;

    private const string LockedMessage =
        "Onyx encrypts your data on this device, but the key is missing. Open Onyx and enter your passphrase.";

    private UserVault? m_Vault;

    /// <summary>The unlocked vault, from the key stored with the credentials; null while locked.</summary>
    public async Task<UserVault?> GetVaultAsync()
    {
        var credentials = await store.LoadAsync();
        if (credentials?.VaultKey is not { } key)
            return null;

        var bytes = Convert.FromBase64String(key);
        if (m_Vault is { } cached && cached.Keys.MasterKey.AsSpan().SequenceEqual(bytes))
            return cached;

        return m_Vault = await UserVault.FromMasterKeyAsync(Crypto, bytes);
    }

    /// <summary>Keeps the master key on this device, so uploads and the dashboard can use it.</summary>
    public async Task StoreVaultKeyAsync(byte[]? masterKey)
    {
        var credentials = await store.LoadAsync() ?? throw new NotSignedInException("Not signed in");
        await store.SaveAsync(credentials with { VaultKey = masterKey is null ? null : Convert.ToBase64String(masterKey) });
        m_Vault = null;
    }

    /// <summary>Cryptography used for the vault; the browser replaces it with WebCrypto.</summary>
    public IOnyxCrypto Crypto { get; init; } = DotNetCrypto.Instance;

    /// <summary>Splits an upload into requests of at most <see cref="MaxEntriesPerUpload"/> entries, keeping each
    /// hour in one request: the server replaces an app's hour with what one request contains.</summary>
    internal static IEnumerable<UsageEntryDto[]> ChunkByHour(IReadOnlyList<UsageEntryDto> entries)
    {
        var chunk = new List<UsageEntryDto>();
        foreach (var hour in entries.GroupBy(e => e.BucketStart).OrderBy(g => g.Key))
        {
            var items = hour.ToList();
            if (chunk.Count > 0 && chunk.Count + items.Count > MaxEntriesPerUpload)
            {
                yield return chunk.ToArray();
                chunk.Clear();
            }

            // A single hour with more entries than the limit (thousands of apps or websites) is split anyway.
            foreach (var part in items.Chunk(MaxEntriesPerUpload))
            {
                if (chunk.Count + part.Length > MaxEntriesPerUpload)
                {
                    yield return chunk.ToArray();
                    chunk.Clear();
                }

                chunk.AddRange(part);
            }
        }

        if (chunk.Count > 0)
            yield return chunk.ToArray();
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

    public async Task<List<string>> GetWebsiteExclusionsAsync(CancellationToken ct = default) =>
        (await SendAsync<WebsiteExclusionsDto>(HttpMethod.Get, "/api/websites/exclusions", null, ct)).Domains;

    public async Task<List<string>> SetWebsiteExclusionsAsync(IReadOnlyList<string> domains, CancellationToken ct = default) =>
        (await SendAsync<WebsiteExclusionsDto>(HttpMethod.Put, "/api/websites/exclusions",
            new WebsiteExclusionsDto(domains.ToList()), ct)).Domains;

    public Task DeleteWebsiteHistoryAsync(CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Delete, "/api/websites/history", null, ct);

    public Task<ProfileDto> GetProfileAsync(CancellationToken ct = default) =>
        SendAsync<ProfileDto>(HttpMethod.Get, "/api/user/profile", null, ct);

    public Task UpdateProfileAsync(string userName, string? phoneNumber, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Put, "/api/user/profile", new UpdateProfileDto(userName, phoneNumber), ct);

    public Task SetAppOverrideAsync(int appId, AppOverrideDto dto, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Put, $"/api/app/{appId}/override", dto, ct);

    public Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, string timeZone, CancellationToken ct = default) =>
        SendAsync<WeeklyReportDto>(HttpMethod.Get,
            $"/api/reports/weekly?weekStart={weekStart:yyyy-MM-dd}&timeZone={Uri.EscapeDataString(timeZone)}", null, ct);

    // End-to-end encryption

    /// <returns>The user's vault, or null if they have not set up encryption yet.</returns>
    public async Task<VaultDto?> GetVaultInfoAsync(CancellationToken ct = default)
    {
        try
        {
            return await SendAsync<VaultDto>(HttpMethod.Get, "/api/e2e/vault", null, ct);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task CreateVaultAsync(CreateVaultDto dto, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Post, "/api/e2e/vault", dto, ct);

    public Task UpdateVaultKeyAsync(UpdateVaultKeyDto dto, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Put, "/api/e2e/vault/key", dto, ct);

    /// <returns>The new settings version.</returns>
    /// <exception cref="VaultConflictException">The settings changed since <paramref name="expectedVersion"/>.</exception>
    public async Task<int> UpdateVaultSettingsAsync(byte[] settings, int expectedVersion, CancellationToken ct = default)
    {
        try
        {
            return (await SendAsync<VaultSettingsVersionDto>(HttpMethod.Put, "/api/e2e/vault/settings",
                new UpdateVaultSettingsDto(settings, expectedVersion), ct)).SettingsVersion;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict)
        {
            throw new VaultConflictException();
        }
    }

    /// <summary>The user's settings, decrypted; null while locked or without a vault.</summary>
    public async Task<VaultSettings?> GetVaultSettingsAsync(CancellationToken ct = default)
    {
        if (await GetVaultAsync() is not { } vault || await GetVaultInfoAsync(ct) is not { } info)
            return null;
        return await vault.DecryptSettingsAsync(info.Settings);
    }

    public Task<List<EncryptedUsageDto>> GetEncryptedUsageAsync(DateTime start, DateTime end, int? deviceId = null,
        CancellationToken ct = default) =>
        SendAsync<List<EncryptedUsageDto>>(HttpMethod.Get,
            $"/api/e2e/usage?start={Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))}" +
            $"&end={Uri.EscapeDataString(end.ToUniversalTime().ToString("O"))}" +
            (deviceId is null ? "" : $"&deviceId={deviceId}"), null, ct);

    /// <summary>Stores encrypted rows for one of the user's devices (replacing rows of the same tag and hour).</summary>
    public async Task<EncryptedUsageUploadResultDto> PutEncryptedUsageAsync(int deviceId,
        IReadOnlyList<EncryptedUsageEntryDto> rows, CancellationToken ct = default)
    {
        var stored = 0;
        var missing = new List<string>();
        foreach (var chunk in rows.Chunk(MaxEntriesPerUpload))
        {
            var result = await SendAsync<EncryptedUsageUploadResultDto>(HttpMethod.Post,
                $"/api/devices/{deviceId}/e2e-usage", new EncryptedUsageUploadDto(chunk.ToList()), ct);
            stored += result.Stored;
            missing.AddRange(result.TagsMissingInfo);
        }

        return new EncryptedUsageUploadResultDto(stored, missing.Distinct().ToList());
    }

    public Task DeleteEncryptedUsageAsync(IReadOnlyList<EncryptedUsageKeyDto> rows, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Post, "/api/e2e/usage/delete", new DeleteEncryptedUsageDto(rows.ToList()), ct);

    public Task<List<EncryptedAppDto>> GetEncryptedAppsAsync(CancellationToken ct = default) =>
        SendAsync<List<EncryptedAppDto>>(HttpMethod.Get, "/api/e2e/apps", null, ct);

    /// <param name="createOnly">Only store apps the server has nothing for (collectors); otherwise replace.</param>
    public Task PutEncryptedAppsAsync(IReadOnlyList<EncryptedAppDto> apps, bool createOnly, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Put, $"/api/e2e/apps?createOnly={(createOnly ? "true" : "false")}", apps, ct);

    public Task<LegacyStatusDto> GetLegacyStatusAsync(CancellationToken ct = default) =>
        SendAsync<LegacyStatusDto>(HttpMethod.Get, "/api/e2e/legacy", null, ct);

    /// <summary>Deletes the plain-text data from before encryption, once it has been stored encrypted.</summary>
    public Task DeleteLegacyDataAsync(CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Delete, "/api/e2e/legacy", null, ct);

    /// <summary>A call without a token (sign-in, registration, password reset).</summary>
    /// <exception cref="HttpRequestException">The server answered with an error; see <see cref="ErrorMessage"/>.</exception>
    public async Task<T> SendAnonymousAsync<T>(HttpMethod method, string path, object? body = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: s_Json);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HttpRequestException("401 Unauthorized: ", null, HttpStatusCode.Unauthorized);
        return await ReadAsync<T>(response, ct);
    }

    /// <summary>Downloads a file with the user's token (the icon proxy).</summary>
    public async Task<(byte[] Data, string? ContentType)> GetBytesAsync(string path, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(forceRefresh: false, ct);
        using var response = await SendOnceAsync(HttpMethod.Get, path, null, token, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}", null,
                response.StatusCode);
        }

        return (await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Stores tokens from a sign-in, keeping the device id when the same account signs in again.</summary>
    public async Task SaveTokensAsync(LoginResultDto tokens, int? deviceId)
    {
        await store.SaveAsync(new CollectorCredentials(tokens.Token, tokens.RefreshToken, deviceId));
        m_Vault = null;
    }

    /// <summary>Any authorized API call, for endpoints without a typed method here.</summary>
    public Task<T> SendAuthorizedAsync<T>(HttpMethod method, string path, object? body = null,
        CancellationToken ct = default) => SendAsync<T>(method, path, body, ct);

    /// <summary>
    /// The user-facing part of an error the server returned: Identity error descriptions, a problem's detail, or
    /// the plain text. Falls back to the exception message.
    /// </summary>
    public static string ErrorMessage(Exception e)
    {
        if (e is not HttpRequestException http)
            return e.Message;
        var i = http.Message.IndexOf(": ", StringComparison.Ordinal);
        if (i < 0)
            return e.Message;

        var body = http.Message[(i + 2)..].Trim();
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return string.Join(" ", root.EnumerateArray()
                    .Select(x => x.TryGetProperty("description", out var d) ? d.GetString() : x.ToString()));
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString() ?? body;
            if (root.TryGetProperty("detail", out var detail))
                return detail.GetString() ?? body;
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrEmpty(body) ? e.Message : body;
    }

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
