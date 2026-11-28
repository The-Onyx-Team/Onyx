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
    private readonly Dictionary<(string App, string Domain, DateTime Hour), Bucket> m_Buckets = new();

    private sealed record Bucket(string AppName, string? Category, TimeSpan Time, int Opens = 0);

    /// <param name="Domain">Website for browser time; null (also in files written before websites existed) for
    /// everything else.</param>
    /// <param name="Opens">Zero in files written before opens were counted.</param>
    private sealed record StoredBucket(string App, string AppName, string? Category, DateTime Hour, TimeSpan Time,
        string? Domain = null, int Opens = 0);

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
    /// <param name="domain">For browsers, the website that was open; null otherwise.</param>
    public void Add(ForegroundApp app, DateTimeOffset start, DateTimeOffset end, string? domain = null)
    {
        lock (m_Lock)
        {
            UsageBuckets.Split(start, end, (hour, time) =>
            {
                var key = (app.Identifier, domain ?? "", hour);
                var total = m_Buckets.TryGetValue(key, out var existing) ? existing.Time + time : time;
                if (total > UsageBuckets.BucketLength)
                    total = UsageBuckets.BucketLength;
                m_Buckets[key] = new Bucket(app.Name, app.Category, total, existing?.Opens ?? 0);
            });
        }
    }

    /// <summary>Counts one open of <paramref name="app"/> (or of the website <paramref name="domain"/> in it) at
    /// <paramref name="at"/>.</summary>
    public void AddOpen(ForegroundApp app, DateTimeOffset at, string? domain = null)
    {
        lock (m_Lock)
        {
            var key = (app.Identifier, domain ?? "", UsageBuckets.HourOf(at));
            m_Buckets[key] = m_Buckets.TryGetValue(key, out var existing)
                ? existing with { Opens = existing.Opens + 1 }
                : new Bucket(app.Name, app.Category, TimeSpan.Zero, 1);
        }
    }

    /// <summary>Everything that should be uploaded next, oldest first.</summary>
    public List<UsageEntryDto> Snapshot()
    {
        lock (m_Lock)
        {
            return m_Buckets
                .Where(b => b.Value.Time > TimeSpan.Zero || b.Value.Opens > 0)
                .Select(b => new UsageEntryDto(b.Key.App, b.Value.AppName, b.Value.Category, b.Key.Hour, b.Value.Time,
                    b.Key.Domain == "" ? null : b.Key.Domain, b.Value.Opens))
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
            // An app's hour is dropped as a whole, or kept as a whole: the server replaces the complete hour on
            // every upload, so a partial re-send would delete the websites left out.
            foreach (var hour in uploaded.Where(e => e.BucketStart < currentHour).GroupBy(e => (e.AppIdentifier, e.BucketStart)))
            {
                var sent = hour.ToDictionary(e => e.Domain ?? "");
                var keys = m_Buckets.Keys.Where(k => k.App == hour.Key.AppIdentifier && k.Hour == hour.Key.BucketStart).ToList();
                var unchanged = keys.All(k => sent.TryGetValue(k.Domain, out var entry)
                                              && m_Buckets[k].Time <= entry.ForegroundTime && m_Buckets[k].Opens <= entry.Opens);
                if (unchanged)
                    foreach (var key in keys)
                        m_Buckets.Remove(key);
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
                .Select(b => new StoredBucket(b.Key.App, b.Value.AppName, b.Value.Category, b.Key.Hour, b.Value.Time,
                    b.Key.Domain == "" ? null : b.Key.Domain, b.Value.Opens))
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
                m_Buckets[(b.App, b.Domain ?? "", DateTime.SpecifyKind(b.Hour, DateTimeKind.Utc))] =
                    new Bucket(b.AppName, b.Category, b.Time, b.Opens);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt file must not stop collection; the data in it is lost either way.
            m_Logger.LogWarning(e, "Could not read pending usage from {Path}, starting empty", m_Path);
        }
    }
}
