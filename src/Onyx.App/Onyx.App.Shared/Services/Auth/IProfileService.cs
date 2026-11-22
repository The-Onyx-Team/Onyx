using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Auth;

/// <summary>The signed-in user's profile: user name, phone number and profile picture.</summary>
/// <remarks>Methods that change something return null on success, otherwise a message for the user.</remarks>
public interface IProfileService
{
    Task<ProfileDto> GetAsync();

    Task<string?> UpdateAsync(string userName, string? phoneNumber);

    /// <param name="data">PNG, JPEG or WebP, at most 512 KB. The UI scales pictures down before uploading.</param>
    Task<string?> SetAvatarAsync(byte[] data, string contentType);

    Task<string?> DeleteAvatarAsync();
}
