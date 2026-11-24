using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Auth;

public interface IUserProvider
{
    /// <returns>The signed-in user. Fails if nobody is signed in.</returns>
    public Task<User> GetRequiredUserAsync();
}

/// <summary>Two-factor authentication of the signed-in user. Failing calls throw <see cref="HttpRequestException"/>
/// (the message for the user via <c>OnyxApiClient.ErrorMessage</c>).</summary>
public interface ITwoFactorService
{
    Task<TwoFactorStatusDto> GetStatusAsync();

    /// <summary>The key for the authenticator app, created on first use.</summary>
    Task<TwoFactorKeyDto> GetKeyAsync();

    /// <returns>The recovery codes.</returns>
    Task<IReadOnlyList<string>> EnableAsync(string code);

    /// <param name="code">A code from the authenticator app or a recovery code.</param>
    Task DisableAsync(string code);

    Task<IReadOnlyList<string>> NewRecoveryCodesAsync(string code);
}
