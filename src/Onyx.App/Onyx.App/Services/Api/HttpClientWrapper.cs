using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using OneOf;
using Onyx.App.Services.Auth;

namespace Onyx.App.Services.Api;

public record HttpError(string Message, int? StatusCode = null);

public record NetworkError(string Message);

public record ParsingError(string Message);

public class HttpClientWrapper
{
    private readonly HttpClient m_HttpClient;
    private readonly ILogger<HttpClientWrapper> m_Logger;
    private readonly AuthenticationStateProvider m_AuthenticationStateProvider;
    
    private static readonly JsonSerializerOptions s_JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HttpClientWrapper(HttpClient httpClient, ILogger<HttpClientWrapper> logger,
        AuthenticationStateProvider authenticationStateProvider)
    {
        m_HttpClient = httpClient;
        m_Logger = logger;
        m_AuthenticationStateProvider = authenticationStateProvider;

        m_AuthenticationStateProvider.AuthenticationStateChanged += async task =>
        {
            var authState = await task;
            var user = authState.User;

            if (user.Identity?.IsAuthenticated == true && await user.GetAuthTokenAsync() is { } token)
            {
                SetAuthToken(token);
            }
            else
            {
                ClearAuthToken();
            }
        };
    }

    public void SetAuthToken(string token)
    {
        m_HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ClearAuthToken()
    {
        m_HttpClient.DefaultRequestHeaders.Authorization = null;
    }

    public void SetBaseUrl(string baseUrl)
    {
        m_HttpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<OneOf<T, HttpError, NetworkError, ParsingError>> GetAsync<T>(string endpoint)
    {
        try
        {
            var response = await m_HttpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                return new HttpError($"HTTP Error: {response.ReasonPhrase}", (int)response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync();

            try
            {
                var result = JsonSerializer.Deserialize<T>(content, s_JsonSerializerOptions);
                if (result is null)
                    return new ParsingError("Deserialization returned null");
                return result;
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Failed to parse response");
                return new ParsingError("Failed to parse JSON response");
            }
        }
        catch (HttpRequestException ex)
        {
            m_Logger.LogError(ex, "Network error occurred");
            return new NetworkError("Network failure: " + ex.Message);
        }
    }

    public Task<OneOf<T, HttpError, NetworkError, ParsingError>> PostAsync<T>(string endpoint, object request) =>
        PostAsync<T>(endpoint, request, request.GetType());
    
    public Task<OneOf<TResponse, HttpError, NetworkError, ParsingError>> PostAsync<TRequest, TResponse>(string endpoint, object request) =>
        PostAsync<TResponse>(endpoint, request, typeof(TRequest));

    private async Task<OneOf<T, HttpError, NetworkError, ParsingError>> PostAsync<T>(
        string endpoint, object request, Type requestType)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, requestType, s_JsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await m_HttpClient.PostAsync(endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                return new HttpError($"HTTP Error: {response.ReasonPhrase}", (int)response.StatusCode);
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                var result = JsonSerializer.Deserialize<T>(responseContent, s_JsonSerializerOptions);
                if (result is null)
                    return new ParsingError("Deserialization returned null");
                return result;
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Failed to parse response");
                return new ParsingError("Failed to parse JSON response");
            }
        }
        catch (HttpRequestException ex)
        {
            m_Logger.LogError(ex, "Network error occurred");
            return new NetworkError("Network failure: " + ex.Message);
        }
    }
}