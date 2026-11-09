using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core;

/// <summary>Splits usage intervals into the hourly UTC buckets the server stores.</summary>
public static class UsageBuckets
{
    public static readonly TimeSpan BucketLength = TimeSpan.FromHours(1);

    public static DateTime HourOf(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Calls <paramref name="add"/> once per hour the interval touches, with the part inside that hour.</summary>
    public static void Split(DateTimeOffset start, DateTimeOffset end, Action<DateTime, TimeSpan> add)
    {
        var cursor = start;
        while (cursor < end)
        {
            var hour = HourOf(cursor);
            var hourEnd = new DateTimeOffset(hour.Add(BucketLength));
            var sliceEnd = end < hourEnd ? end : hourEnd;
            add(hour, sliceEnd - cursor);
            cursor = sliceEnd;
        }
    }

    /// <summary>
    /// Sums foreground intervals into entries, one per app and hour. Durations are capped at one hour, which
    /// overlapping intervals reported by a platform could otherwise exceed.
    /// </summary>
    public static List<UsageEntryDto> FromIntervals(IEnumerable<(ForegroundApp App, DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        var totals = new Dictionary<(string, DateTime), (ForegroundApp App, TimeSpan Time)>();

        foreach (var (app, start, end) in intervals)
        {
            Split(start, end, (hour, time) =>
            {
                var key = (app.Identifier, hour);
                totals[key] = totals.TryGetValue(key, out var existing)
                    ? (app, existing.Time + time)
                    : (app, time);
            });
        }

        return totals
            .Where(t => t.Value.Time > TimeSpan.Zero)
            .Select(t => new UsageEntryDto(t.Key.Item1, t.Value.App.Name, t.Value.App.Category, t.Key.Item2,
                t.Value.Time < BucketLength ? t.Value.Time : BucketLength))
            .OrderBy(e => e.BucketStart)
            .ToList();
    }
}
