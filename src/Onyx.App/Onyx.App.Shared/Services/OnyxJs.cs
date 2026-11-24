using Microsoft.JSInterop;

namespace Onyx.App.Shared.Services;

/// <summary>The helpers in <c>wwwroot/js/onyx.js</c> (works in the browser and in the apps' web view).</summary>
public sealed class OnyxJs(IJSRuntime js, IPlatformService platform) : IAsyncDisposable
{
    private Task<IJSObjectReference>? m_Module;

    public async ValueTask<T> InvokeAsync<T>(string function, params object?[] args) =>
        await (await ModuleAsync()).InvokeAsync<T>(function, args);

    public async ValueTask InvokeVoidAsync(string function, params object?[] args) =>
        await (await ModuleAsync()).InvokeVoidAsync(function, args);

    /// <summary>Scales a picture to fit <paramref name="size"/> × <paramref name="size"/> pixels, as PNG.</summary>
    public ValueTask<byte[]> ResizeImageAsync(byte[] image, int size) =>
        InvokeAsync<byte[]>("resizeImage", image, size);

    /// <summary>Scales a picture to fit <paramref name="size"/> × <paramref name="size"/> pixels, as JPEG (photos).</summary>
    public ValueTask<byte[]> ResizeImageToJpegAsync(byte[] image, int size, double quality = 0.8) =>
        InvokeAsync<byte[]>("resizeImage", image, size, "image/jpeg", quality);

    private Task<IJSObjectReference> ModuleAsync() =>
        m_Module ??= js.InvokeAsync<IJSObjectReference>("import",
            // The browser gets a per-build address so it never runs an old copy; the apps clear their web view's
            // cache after an update instead (their file server may not ignore query strings).
            platform.IsMaui()
                ? "./_content/Onyx.App.Shared/js/onyx.js"
                : $"./_content/Onyx.App.Shared/js/onyx.js?v={AppVersion.CacheToken}").AsTask();

    public async ValueTask DisposeAsync()
    {
        if (m_Module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await (await m_Module).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
