using System.Text.Json;
using Onyx.App.Shared.Services;

namespace Onyx.App.Services;

public class MauiStorage : IStorage
{
    public async ValueTask<bool> ContainKeyAsync(string key)
    {
        return await SecureStorage.Default.GetAsync(key) != null;
    }

    public async ValueTask<T?> GetItemAsync<T>(string key)
    {
        var item = await SecureStorage.Default.GetAsync(key);

        return item is null ? default : JsonSerializer.Deserialize<T>(item);
    }

    public async ValueTask SetItemAsync<T>(string key, T value)
    {
        var item = JsonSerializer.Serialize(value);

        await SecureStorage.Default.SetAsync(key, item);
    }
}