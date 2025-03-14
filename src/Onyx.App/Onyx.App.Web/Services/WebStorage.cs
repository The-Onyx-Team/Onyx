using Blazored.LocalStorage;
using Onyx.App.Shared.Services;

namespace Onyx.App.Web.Services;

public class WebStorage(ILocalStorageService storage, IHttpContextAccessor contextAccessor) : IStorage
{
    public ValueTask<bool> ContainKeyAsync(string key)
    {
        if (contextAccessor.HttpContext is { Response.HasStarted: true })
            return storage.ContainKeyAsync(key);
        return new ValueTask<bool>(false);
    }

    public ValueTask<T?> GetItemAsync<T>(string key)
    {
        if (contextAccessor.HttpContext is { Response.HasStarted: true })
            return storage.GetItemAsync<T>(key);
        return new ValueTask<T?>((T)default!);
    }

    public ValueTask SetItemAsync(string key, bool value)
    {
        if (contextAccessor.HttpContext is { Response.HasStarted: true })
            return storage.SetItemAsync(key, value);
        return new ValueTask();
    }
}