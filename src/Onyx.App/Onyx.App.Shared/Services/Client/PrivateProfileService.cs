using Onyx.App.Shared.Services.Auth;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Client;

/// <summary>
/// The end-to-end encrypted part of the profile: real name, phone number and profile picture. Accounts from before
/// it existed have the phone number and picture in plain text on the server; the first <see cref="GetAsync"/>
/// after unlocking moves them in, and saving it makes the server delete the plain copies.
/// </summary>
public sealed class PrivateProfileService(OnyxApiClient api, VaultService vault, IProfileService profiles, OnyxJs js)
{
    // Cached per unlocked vault, so another account signing in on this device never sees it.
    private ProfilePayload? m_Profile;
    private UserVault? m_For;

    /// <exception cref="VaultLockedException">The vault is locked.</exception>
    public async Task<ProfilePayload> GetAsync(bool refresh = false)
    {
        var v = await vault.GetVaultAsync();
        if (m_Profile is not null && !refresh && ReferenceEquals(m_For, v))
            return m_Profile;

        var stored = await api.SendAuthorizedAsync<EncryptedProfileDto>(HttpMethod.Get, "/api/e2e/profile");
        var profile = stored.Data is { Length: > 0 } data ? await v.DecryptProfileAsync(data) : new ProfilePayload();

        // Move what is still stored in plain text into the encrypted profile.
        var plain = await profiles.GetAsync();
        if (plain.PhoneNumber is { Length: > 0 } || plain.AvatarDataUrl is not null)
        {
            var (avatar, type) = ParseDataUrl(plain.AvatarDataUrl);
            profile = profile with
            {
                PhoneNumber = profile.PhoneNumber ?? NullIfEmpty(plain.PhoneNumber),
                Avatar = profile.Avatar ?? avatar,
                AvatarContentType = profile.Avatar is not null ? profile.AvatarContentType : type
            };
            await SaveAsync(profile);
        }

        m_For = v;
        return m_Profile = profile;
    }

    /// <summary>Size of the picture friends get with the shared numbers.</summary>
    public const int ThumbnailSize = 64;

    /// <summary>Encrypts and stores the profile (with a new thumbnail when the picture changed).</summary>
    public async Task SaveAsync(ProfilePayload profile)
    {
        if (profile.Avatar is null)
            profile = profile with { Thumbnail = null };
        else if (profile.Thumbnail is null || !ReferenceEquals(profile.Avatar, m_Profile?.Avatar))
        {
            try
            {
                profile = profile with { Thumbnail = await js.ResizeImageToJpegAsync(profile.Avatar, ThumbnailSize, 0.75) };
            }
            catch (Exception)
            {
                profile = profile with { Thumbnail = null }; // friends see the initial instead
            }
        }

        var v = await vault.GetVaultAsync();
        await api.SendAuthorizedAsync<object?>(HttpMethod.Put, "/api/e2e/profile",
            new EncryptedProfileDto(await v.EncryptProfileAsync(profile)));
        m_Profile = profile;
        m_For = v;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    internal static (byte[]? Data, string? Type) ParseDataUrl(string? url)
    {
        if (url is null || !url.StartsWith("data:", StringComparison.Ordinal))
            return (null, null);
        var comma = url.IndexOf(',');
        var semicolon = url.IndexOf(';');
        if (comma < 0 || semicolon < 0 || semicolon > comma)
            return (null, null);
        try
        {
            return (Convert.FromBase64String(url[(comma + 1)..]), url[5..semicolon]);
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }
}
