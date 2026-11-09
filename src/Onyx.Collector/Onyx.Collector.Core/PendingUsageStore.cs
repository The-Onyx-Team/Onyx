using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core;

/// <summary>
/// Hourly usage totals that have not been confirmed by the server yet, persisted to a JSON file so nothing is
/// lost while offline or across restarts.
/// </summary>
/// <remarks>
/// The tracker adds foreground time as it is observed. Every upload sends the full totals; the server replaces
/// buckets it already has, so the still-growing current hour can be re-sent on every sync. After a successful
/// upload, finished hours are dropped unless they changed in the meantime.
/// </remarks>
public sealed class PendingUsageStore
{
    private readonly string? m_Path;
    private readonly ILogger m_Logger;
    private readonly Lock m_Lock = new();
    private readonly Dictionary<(string App, DateTime Hour), Bucket> m_Buckets = new();

    private sealed record Bucket(string AppName, string? Category, TimeSpan Time);

    private sealed record StoredBucket(string App, string AppName, string? Category, DateTime Hour, TimeSpan Time);

    /// <param name="path">File to persist to; null keeps everything in memory (tests).</param>
    public PendingUsageStore(string? path, ILogger? logger = null)
    {
        m_Path = path;
        m_Logger = logger ?? NullLogger.Instance;
        Load();
    }

    public int Count
    {
        get
        {
            lock (m_Lock) return m_Buckets.Count;
        }
    }

    /// <summary>Adds foreground time of <paramref name="app"/> between two instants, split across hours.</summary>
    public void Add(ForegroundApp app, DateTimeOffset start, DateTimeOffset end)
    {
        lock (m_Lock)
        {
            UsageBuckets.Split(start, end, (hour, time) =>
            {
                var key = (app.Identifier, hour);
                var total = m_Buckets.TryGetValue(key, out var existing) ? existing.Time + time : time;
                if (total > UsageBuckets.BucketLength)
                    total = UsageBuckets.BucketLength;
                m_Buckets[key] = new Bucket(app.Name, app.Category, total);
            });
        }
    }

    /// <summary>Everything that should be uploaded next, oldest first.</summary>
    public List<UsageEntryDto> Snapshot()
    {
        lock (m_Lock)
        {
            return m_Buckets
                .Where(b => b.Value.Time > TimeSpan.Zero)
                .Select(b => new UsageEntryDto(b.Key.App, b.Value.AppName, b.Value.Category, b.Key.Hour, b.Value.Time))
                .OrderBy(e => e.BucketStart)
                .ToList();
        }
    }

    /// <summary>
    /// Marks an uploaded snapshot as stored. Hours before <paramref name="currentHour"/> are removed unless they
    /// grew after the snapshot was taken; the current hour stays and is sent again next time.
    /// </summary>
    public void Acknowledge(IEnumerable<UsageEntryDto> uploaded, DateTime currentHour)
    {
        lock (m_Lock)
        {
            foreach (var entry in uploaded)
            {
                var key = (entry.AppIdentifier, entry.BucketStart);
                if (entry.BucketStart < currentHour
                    && m_Buckets.TryGetValue(key, out var bucket)
                    && bucket.Time <= entry.ForegroundTime)
                {
                    m_Buckets.Remove(key);
                }
            }
        }
    }

    /// <summary>Writes the store to disk (atomically, via a temporary file).</summary>
    public void Save()
    {
        if (m_Path is null)
            return;

        List<StoredBucket> data;
        lock (m_Lock)
        {
            data = m_Buckets
                .Select(b => new StoredBucket(b.Key.App, b.Value.AppName, b.Value.Category, b.Key.Hour, b.Value.Time))
                .ToList();
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(m_Path))!);
            var temp = m_Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            File.Move(temp, m_Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            m_Logger.LogWarning(e, "Could not save pending usage to {Path}", m_Path);
        }
    }

    private void Load()
    {
        if (m_Path is null || !File.Exists(m_Path))
            return;

        try
        {
            var data = JsonSerializer.Deserialize<List<StoredBucket>>(File.ReadAllText(m_Path)) ?? [];
            foreach (var b in data)
                m_Buckets[(b.App, DateTime.SpecifyKind(b.Hour, DateTimeKind.Utc))] = new Bucket(b.AppName, b.Category, b.Time);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt file must not stop collection; the data in it is lost either way.
            m_Logger.LogWarning(e, "Could not read pending usage from {Path}, starting empty", m_Path);
        }
    }
}
