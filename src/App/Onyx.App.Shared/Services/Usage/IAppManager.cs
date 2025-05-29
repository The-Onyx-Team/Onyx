using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Usage;

public interface IAppManager
{
    /// <returns>ID of generated App</returns>
    public Task<int> RegisterAppAsync(string name, byte[] iconBitmap);

    public Task<List<AppDto>> GetAppsAsync();
}