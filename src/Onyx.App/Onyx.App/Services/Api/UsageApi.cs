using Microsoft.Extensions.Logging;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Services.Api;

public class UsageApi(HttpClientWrapper httpClientWrapper, ILogger<AuthApi> logger)
    : ApiBase<AuthApi>(httpClientWrapper, logger)
{
    public class ResultBool{
        public bool Result { get; set; }}
    public async Task UploadDataAsync(int deviceId, List<UsageDto> usageData)
    {
        var endpoint = $"/api/data/usage/upload?deviceId={deviceId}";
        var result =
            await m_HttpClientWrapper.PostAsync<ResultBool>(endpoint, usageData);

        result.Match(
            success => success,
            error => null,
            error => HandleNetworkError<ResultBool>(error, endpoint),
            error => HandleParsingError<ResultBool>(error, endpoint));
    }

    
}