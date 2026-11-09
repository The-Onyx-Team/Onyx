using System.Text.Json;
using Onyx.Collector.Core;

namespace Onyx.Collector.Linux;

/// <summary>Agent settings, in $XDG_CONFIG_HOME/onyx/agent.json.</summary>
internal sealed record AgentConfig(string ServerUrl, string? DeviceName = null)
{
    public string EffectiveDeviceName => string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName;
}

internal static class AgentFiles
{
    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ConfigDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "onyx");

    public static string StateDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"), "onyx");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "agent.json");
    public static string CredentialsPath => Path.Combine(ConfigDirectory, "credentials.json");
    public static string PendingPath => Path.Combine(StateDirectory, "pending.json");

    public static AgentConfig? LoadConfig() =>
        File.Exists(ConfigPath) ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(ConfigPath), s_Json) : null;

    public static void SaveConfig(AgentConfig config) => WritePrivate(ConfigPath, JsonSerializer.Serialize(config, s_Json));

    /// <summary>Writes a file only the current user can read (it may hold tokens).</summary>
    public static void WritePrivate(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, new FileStreamOptions
               {
                   Mode = FileMode.Create,
                   Access = FileAccess.Write,
                   UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
               }))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(content);
        }

        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Tokens and device id in $XDG_CONFIG_HOME/onyx/credentials.json, mode 0600.</summary>
internal sealed class FileCredentialStore : ICredentialStore
{
    public Task<CollectorCredentials?> LoadAsync()
    {
        if (!File.Exists(AgentFiles.CredentialsPath))
            return Task.FromResult<CollectorCredentials?>(null);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<CollectorCredentials>(File.ReadAllText(AgentFiles.CredentialsPath)));
        }
        catch (JsonException)
        {
            return Task.FromResult<CollectorCredentials?>(null);
        }
    }

    public Task SaveAsync(CollectorCredentials credentials)
    {
        AgentFiles.WritePrivate(AgentFiles.CredentialsPath, JsonSerializer.Serialize(credentials));
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        File.Delete(AgentFiles.CredentialsPath);
        return Task.CompletedTask;
    }
}
