using System.Net;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Client;

/// <summary>The signed-in side of linking a watch by code (<see cref="DeviceLink"/>).</summary>
public sealed class DeviceLinkService(OnyxApiClient api, VaultService vault)
{
    /// <summary>Signs the device showing <paramref name="typedCode"/> in to this account and gives it the
    /// encryption key, encrypted for that device only.</summary>
    /// <returns>Null on success, otherwise a message for the user.</returns>
    public async Task<string?> LinkAsync(string typedCode)
    {
        var code = DeviceLink.Normalize(typedCode);
        if (code.Length != DeviceLink.CodeLength)
            return $"The code has {DeviceLink.CodeLength} characters, e.g. 7K2QM-X9D4R.";

        LinkInfoDto info;
        try
        {
            info = await api.SendAuthorizedAsync<LinkInfoDto>(HttpMethod.Get, $"/api/link/{code}");
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return "No device shows this code. Codes are valid for 10 minutes; check it or start again on the watch.";
        }

        // The code is the fingerprint of the device's key: a key from anyone else (also the server) doesn't match.
        if (DeviceLink.CodeOf(info.PublicKey) != code)
            return "The server returned a different device for this code. Nothing was sent.";

        var crypto = api.Crypto;
        var (privateKey, publicKey) = await crypto.CreateKeyPairAsync();
        var shared = await crypto.SharedKeyAsync(privateKey, publicKey, info.PublicKey);
        var masterKey = (await vault.GetVaultAsync()).Keys.MasterKey;
        var encrypted = await crypto.EncryptAsync(shared, masterKey, DeviceLink.Context(code));
        await api.SendAuthorizedAsync<object?>(HttpMethod.Post, $"/api/link/{code}/complete",
            new CompleteLinkDto(publicKey, encrypted));
        return null;
    }
}
