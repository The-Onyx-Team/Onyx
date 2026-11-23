using Onyx.App.Shared.Services;

namespace Onyx.App.Services;

/// <summary>Files go to the share sheet (save to Files, send by email, open in a PDF viewer, ...).</summary>
public sealed class MauiFileSaver : IFileSaver
{
    public async Task SaveAsync(string fileName, string contentType, byte[] data)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, data);
        await MainThread.InvokeOnMainThreadAsync(() =>
            Share.Default.RequestAsync(new ShareFileRequest(fileName, new ShareFile(path, contentType))));
    }
}

/// <summary>The apps download pictures themselves; the server never sees the address.</summary>
public sealed class DirectIconFetcher : IIconFetcher
{
    private static readonly HttpClient s_Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public bool UsesServer => false;

    public async Task<byte[]> FetchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Enter an http or https address.");
        try
        {
            using var response = await s_Http.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The site answered {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > 1024 * 1024)
                throw new InvalidOperationException("The picture is larger than 1 MB.");
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("The picture could not be downloaded.");
        }
    }
}
