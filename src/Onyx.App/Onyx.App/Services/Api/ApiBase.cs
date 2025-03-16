using System.Web;

namespace Onyx.App.Services.Api;

public class ApiBase
{
    private readonly HttpClientWrapper m_HttpClientWrapper;

    public ApiBase(HttpClientWrapper httpClientWrapper)
    {
        m_HttpClientWrapper = httpClientWrapper;
        m_HttpClientWrapper.SetBaseUrl("http://localhost:5262"); // TODO
    }

    public void SetAuthToken(string token) => m_HttpClientWrapper.SetAuthToken(token);

    protected async Task GetAsync<T>(string endpoint, (string key, string value)[]? parameters = null)
    {
        var result = await m_HttpClientWrapper.GetAsync<T>(BuildUrl(endpoint, parameters));

        result.Switch(
            success => Console.WriteLine($"Success: {success}"),
            httpError => Console.WriteLine($"HTTP Error: {httpError.Message} (Status: {httpError.StatusCode})"),
            networkError => Console.WriteLine($"Network Error: {networkError.Message}"),
            parsingError => Console.WriteLine($"Parsing Error: {parsingError.Message}")
        );
    }
    
    protected async Task PostAsync<TIn, TOut>(string endpoint, TIn? body = null, (string key, string value)[]? parameters = null) where TIn : class
    {
        var result = await m_HttpClientWrapper.PostAsync<TIn, TOut>(BuildUrl(endpoint, parameters), body!);

        result.Switch(
            success => Console.WriteLine($"Success: {success}"),
            httpError => Console.WriteLine($"HTTP Error: {httpError.Message} (Status: {httpError.StatusCode})"),
            networkError => Console.WriteLine($"Network Error: {networkError.Message}"),
            parsingError => Console.WriteLine($"Parsing Error: {parsingError.Message}")
        );
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