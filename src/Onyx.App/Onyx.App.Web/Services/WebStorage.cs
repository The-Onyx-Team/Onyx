using Blazored.LocalStorage;
using Onyx.App.Shared.Services;

namespace Onyx.App.Web.Services;

public class WebStorage(ILocalStorageService storage) : IStorage
{
    public ValueTask<bool> ContainKeyAsync(string key)
    {
        return storage.ContainKeyAsync(key);
    }

    public ValueTask<T?> GetItemAsync<T>(string key)
    {
        return storage.GetItemAsync<T>(key);
    }

    public ValueTask SetItemAsync(string key, bool value)
    {
        return storage.SetItemAsync(key, value);
    }
}