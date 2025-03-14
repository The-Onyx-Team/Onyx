namespace Onyx.App.Shared.Services;

public interface IStorage
{
    ValueTask<bool> ContainKeyAsync(string key);
    ValueTask<T?> GetItemAsync<T>(string key);
    ValueTask SetItemAsync(string key, bool value);
}