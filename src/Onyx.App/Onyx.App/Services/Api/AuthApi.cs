using Microsoft.Extensions.Logging;
using Onyx.App.Shared.Services.Auth;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Services.Api;

public class AuthApi(HttpClientWrapper httpClientWrapper, ILogger<AuthApi> logger)
    : ApiBase<AuthApi>(httpClientWrapper, logger)
{
    public async Task<LoginResultDto?> LoginAsync(string email, string password)
    {
        const string endpoint = "/api/auth/login";
        var result =
            await m_HttpClientWrapper.PostAsync<LoginResultDto>(endpoint, new LoginDto(email, password));

        return result.Match(
            success => success,
            error => null,
            error => HandleNetworkError<LoginResultDto>(error, endpoint),
            error => HandleParsingError<LoginResultDto>(error, endpoint));
    }

    public async Task<RegisterResultDto?> RegisterAsync(string name, string email, string password)
    {
        const string endpoint = "/api/auth/register";
        var result =
            await m_HttpClientWrapper.PostAsync<RegisterResultDto>(endpoint, new RegisterDto(name, email, password));

        return result.Match(
            success => new RegisterResultDto(true, null),
            error => new RegisterResultDto(false, error.Message),
            error => HandleNetworkError<RegisterResultDto>(error, endpoint),
            error => HandleParsingError<RegisterResultDto>(error, endpoint));
    }
}