using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Client;

public sealed class ApiDeviceManager(OnyxApiClient client) : IDeviceManager
{
    public Task<List<DeviceDto>> GetDevices() => client.GetDevicesAsync();

    public Task<DeviceDto> RegisterDeviceAsync(string deviceName, string? platform) =>
        client.RegisterDeviceAsync(deviceName, platform);

    public Task UnregisterDeviceAsync(int deviceId) => client.DeleteDeviceAsync(deviceId);
}

public sealed class ApiProfileService(OnyxApiClient client) : IProfileService
{
    public Task<ProfileDto> GetAsync() => client.GetProfileAsync();

    public Task<string?> UpdateAsync(string userName, string? phoneNumber) =>
        Try(() => client.UpdateProfileAsync(userName, phoneNumber));

    public Task<string?> SetAvatarAsync(byte[] data, string contentType) =>
        Try(() => client.SetAvatarAsync(data, contentType));

    public Task<string?> DeleteAvatarAsync() => Try(() => client.DeleteAvatarAsync());

    public Task<string?> ConsentAsync() =>
        Try(() => client.SendAuthorizedAsync<object?>(HttpMethod.Post, "/api/user/consent",
            new ConsentDto(PrivacyPolicy.Version)));

    private static async Task<string?> Try(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException or TaskCanceledException)
        {
            return OnyxApiClient.ErrorMessage(e);
        }
    }
}

public sealed class ClientUserProvider(AuthenticationStateProvider authState, OnyxApiClient client) : IUserProvider
{
    public async Task<User> GetRequiredUserAsync()
    {
        var principal = (await authState.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true)
            throw new NotSignedInException("Not signed in");

        var profile = await client.GetProfileAsync();
        return new User
        {
            Id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Name = profile.UserName,
            Email = profile.Email,
            PhoneNumber = profile.PhoneNumber,
            Has2Fa = profile.TwoFactorEnabled
        };
    }

    public Task<string> GetAuthenticatorKeyAsync() =>
        client.SendAuthorizedAsync<string>(HttpMethod.Get, "/api/user/2fa/key");
}

/// <summary>Downloads pictures through the server (the browser can't fetch most other sites itself).</summary>
public sealed class ProxyIconFetcher(OnyxApiClient client) : IIconFetcher
{
    public bool UsesServer => true;

    public async Task<byte[]> FetchAsync(string url)
    {
        try
        {
            return (await client.GetBytesAsync($"/api/icon-proxy?url={Uri.EscapeDataString(url)}")).Data;
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(OnyxApiClient.ErrorMessage(e));
        }
    }
}
