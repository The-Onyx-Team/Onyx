using Onyx.App.Shared.Services;
using Onyx.App.Shared.Services.Usage;

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
    private static readonly HttpClient s_Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(15) };

    public bool UsesServer => false;

    public async Task<byte[]> FetchAsync(string url)
    {
        try
        {
            return (await IconDownloader.DownloadAsync(s_Http, IconDownloader.Normalize(url))).Data;
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"The picture could not be downloaded: {e.Message}");
        }
    }
}
