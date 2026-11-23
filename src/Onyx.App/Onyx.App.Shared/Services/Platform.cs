namespace Onyx.App.Shared.Services;

/// <summary>Where the encryption key is kept on this device.</summary>
public interface IVaultKeyOptions
{
    /// <summary>Whether the user can choose (browser). The apps always keep the key: their background upload
    /// needs it.</summary>
    bool CanChoose { get; }

    /// <summary>Keep the key after the browser is closed; otherwise the passphrase is asked again.</summary>
    bool Remember { get; set; }
}

/// <summary>Hands a file to the user (download in the browser, share sheet in the apps).</summary>
public interface IFileSaver
{
    Task SaveAsync(string fileName, string contentType, byte[] data);
}

/// <summary>Downloads a picture from a URL (app icons).</summary>
public interface IIconFetcher
{
    /// <summary>Whether the server downloads it (the browser can't fetch other sites); shown to the user.</summary>
    bool UsesServer { get; }

    /// <exception cref="InvalidOperationException">With a message for the user.</exception>
    Task<byte[]> FetchAsync(string url);
}
