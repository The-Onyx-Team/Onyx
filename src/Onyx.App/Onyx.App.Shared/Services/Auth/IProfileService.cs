using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Auth;

/// <summary>The signed-in user's account as the server sees it: user name, email, consent. The real name, phone
/// number and picture are end-to-end encrypted (<c>PrivateProfileService</c>).</summary>
/// <remarks>Methods that change something return null on success, otherwise a message for the user.</remarks>
public interface IProfileService
{
    Task<ProfileDto> GetAsync();

    /// <param name="phoneNumber">Only for accounts without encryption; null leaves it unchanged.</param>
    Task<string?> UpdateAsync(string userName, string? phoneNumber);

    /// <summary>Records that the user agreed to the current <see cref="PrivacyPolicy"/>.</summary>
    Task<string?> ConsentAsync();
}
