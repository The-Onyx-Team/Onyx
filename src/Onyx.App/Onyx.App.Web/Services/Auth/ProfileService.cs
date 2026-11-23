using Microsoft.AspNetCore.Identity;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.App.Web.Services.Auth;

/// <summary>Profile DTOs for the <c>/api/user</c> endpoints.</summary>
public static class ProfileService
{
    public static ProfileDto ToDto(ApplicationUser user, bool hasPassword = true) => new(
        user.UserName, user.Email, user.PhoneNumber, user.EmailConfirmed, user.PhoneNumberConfirmed,
        user.TwoFactorEnabled,
        user.Avatar is { Length: > 0 } avatar && user.AvatarContentType is { } type
            ? $"data:{type};base64,{Convert.ToBase64String(avatar)}"
            : null,
        user.PrivacyConsentAt is { } consent ? DateTime.SpecifyKind(consent, DateTimeKind.Utc) : null,
        user.PrivacyPolicyVersion,
        hasPassword);
}

/// <summary>Rules for profile pictures, shared by the web UI and the API.</summary>
public static class Avatar
{
    public static readonly IReadOnlyDictionary<string, byte[][]> Signatures = new Dictionary<string, byte[][]>
    {
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
        // RIFF....WEBP: the bytes at 0 and 8 are checked separately.
        ["image/webp"] = [[0x52, 0x49, 0x46, 0x46]],
    };

    /// <returns>Null if the picture is acceptable, otherwise the reason.</returns>
    public static string? Validate(byte[]? data, string? contentType)
    {
        if (data is not { Length: > 0 })
            return "The picture is empty.";
        if (data.Length > ApplicationUser.MaxAvatarBytes)
            return $"The picture is larger than {ApplicationUser.MaxAvatarBytes / 1024} KB.";
        if (contentType is null || !Signatures.TryGetValue(contentType, out var signatures))
            return "Use a PNG, JPEG or WebP picture.";
        if (!signatures.Any(s => data.AsSpan().StartsWith(s)))
            return "The file is not a valid picture of that type.";
        if (contentType == "image/webp" && (data.Length < 12 || !data.AsSpan(8, 4).SequenceEqual("WEBP"u8)))
            return "The file is not a valid picture of that type.";
        return null;
    }
}
