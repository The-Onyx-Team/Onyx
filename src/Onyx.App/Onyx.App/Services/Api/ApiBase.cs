using System.Web;
using Microsoft.Extensions.Logging;
using OneOf;

namespace Onyx.App.Services.Api;

public abstract class ApiBase<T> where T : ApiBase<T>
{
    protected readonly HttpClientWrapper m_HttpClientWrapper;
    protected readonly ILogger<T> m_Logger;

    // ReSharper disable once ContextualLoggerProblem
    protected ApiBase(HttpClientWrapper httpClientWrapper, ILogger<T> logger)
    {
        m_HttpClientWrapper = httpClientWrapper;
        m_Logger = logger;
        m_HttpClientWrapper.SetBaseUrl("http://localhost:5262"); // TODO
    }

    public void SetAuthToken(string token) => m_HttpClientWrapper.SetAuthToken(token);

    protected async Task<TResult> GetAsync<TResult>(string endpoint, (string key, string value)[]? parameters = null)
    {
        var result = await m_HttpClientWrapper.GetAsync<TResult>(BuildUrl(endpoint, parameters));
        return HandleResult(result, endpoint);
    }

    protected async Task<TResponse> PostAsync<TResponse>(string endpoint, object? body = null, (string key, string value)[]? parameters = null)
    {
        var result = await m_HttpClientWrapper.PostAsync<TResponse>(BuildUrl(endpoint, parameters), body!);
        return HandleResult(result, endpoint);
    }

    protected Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest? body = null,
        (string key, string value)[]? parameters = null) where TRequest : class =>
        PostAsync<TResponse>(endpoint, body, parameters);

    protected TResult HandleResult<TResult>(OneOf<TResult, HttpError, NetworkError, ParsingError> result, string endpoint)
    {
        TResult? response = default;

        result.Switch(
            success => response = success,
            error => HandleHttpError(error, endpoint),
            error => HandleNetworkError(error, endpoint),
            error => HandleParsingError(error, endpoint)
        );

        return response!;
    }

    protected void HandleParsingError(ParsingError parsingError, string endpoint)
    {
        m_Logger.LogError("ParsingError fetching Resource {Endpoint}: {Message}", endpoint, parsingError.Message);
        _ = AlertService.ShowErrorAsync("ParsingError",
            $"ParsingError fetching Resource {endpoint}: {parsingError.Message}");
    }

    protected void HandleNetworkError(NetworkError networkError, string endpoint)
    {
        m_Logger.LogError("NetworkError fetching Resource {Endpoint}: {Message}", endpoint, networkError.Message);
        _ = AlertService.ShowErrorAsync("NetworkError",
            $"NetworkError fetching Resource {endpoint}: {networkError.Message}");
    }

    protected void HandleHttpError(HttpError httpError, string endpoint)
    {
        m_Logger.LogError("HttpError {StatusCode} fetching Resource {Endpoint}: {Message}", httpError.StatusCode,
            endpoint, httpError.Message);
        _ = AlertService.ShowErrorAsync("HttpError",
            $"HttpError {httpError.StatusCode} fetching Resource {endpoint}: {httpError.Message}");
    }
    
    protected TResult? HandleParsingError<TResult>(ParsingError parsingError, string endpoint)
    {
        HandleParsingError(parsingError, endpoint);
        return default;
    }

    protected TResult? HandleNetworkError<TResult>(NetworkError networkError, string endpoint)
    {
        HandleNetworkError(networkError, endpoint);
        return default;
    }

    protected TResult? HandleHttpError<TResult>(HttpError httpError, string endpoint)
    {
        HandleHttpError(httpError, endpoint);
        return default;
    }

    private static string BuildUrl(string endpoint, (string key, string value)[]? parameters = null)
    {
        var uriBuilder = new UriBuilder(new Uri(endpoint));
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                query[key] = value;
            }
        }

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }
}