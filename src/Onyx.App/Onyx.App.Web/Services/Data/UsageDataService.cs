using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Services.Data;

public class UsageDataService(ApplicationDbContext dbContext, CurrentUser currentUser, TimeProvider timeProvider)
    : IUsageDataService
{
    /// <summary>Upper bound for one upload; a collector that has been offline for weeks sends several.</summary>
    public const int MaxEntriesPerUpload = 5000;

    private const int MaxIdentifierLength = 200;
    private const int MaxAppNameLength = 200;
    private const int MaxCategoryLength = 500;

    public async Task<List<UsageDto>> GetUsageDataAsync(DateTime startTime, DateTime endTime, int? deviceId = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        var start = ToUtc(startTime);
        var end = ToUtc(endTime);

        var query = dbContext.Usages
            .Where(u => u.Device!.UserId == userId)
            .Where(u => u.Date >= start && u.Date < end);

        if (deviceId is not null)
            query = query.Where(u => u.DeviceId == deviceId);

        var rows = await query
            .Select(u => new UsageDto
            {
                AppId = u.AppId,
                AppName = u.App!.Name!,
                Category = u.App.Category != null ? u.App.Category.Name : null,
                DeviceId = u.DeviceId,
                DeviceName = u.Device!.Name!,
                Date = u.Date,
                TimeSpan = u.Duration
            })
            .ToListAsync();

        // SQLite hands DateTimes back with Kind=Unspecified; they are stored as UTC.
        foreach (var row in rows)
            row.Date = DateTime.SpecifyKind(row.Date, DateTimeKind.Utc);

        return rows;
    }

    public async Task<UsageUploadResultDto> UploadUsageAsync(int deviceId, IReadOnlyList<UsageEntryDto> entries)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();

        var device = await dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId)
                     ?? throw new KeyNotFoundException("Device not found");

        var buckets = Validate(entries);

        try
        {
            return await StoreAsync(device, buckets);
        }
        catch (DbUpdateException)
        {
            // Another upload created one of the same apps or buckets concurrently; the unique indexes rejected
            // ours. Everything that conflicted exists now, so a second pass updates instead of inserting.
            dbContext.ChangeTracker.Clear();
            device = await dbContext.Devices.FirstAsync(d => d.Id == deviceId);
            return await StoreAsync(device, buckets);
        }
    }

    private async Task<UsageUploadResultDto> StoreAsync(Device device, List<Bucket> buckets)
    {
        var apps = await GetOrCreateAppsAsync(buckets);

        if (buckets.Count > 0)
        {
            var appIds = apps.Values.Select(a => a.Id).ToList();
            var first = buckets.Min(b => b.Start);
            var last = buckets.Max(b => b.Start);

            var existing = await dbContext.Usages
                .Where(u => u.DeviceId == device.Id && appIds.Contains(u.AppId) && u.Date >= first && u.Date <= last)
                .ToListAsync();
            var byKey = existing.ToDictionary(u => (u.AppId, DateTime.SpecifyKind(u.Date, DateTimeKind.Utc)));

            foreach (var bucket in buckets)
            {
                var appId = apps[bucket.Identifier].Id;

                if (byKey.TryGetValue((appId, bucket.Start), out var usage))
                {
                    usage.Duration = bucket.Duration;
                    continue;
                }

                usage = new Usage
                {
                    Id = Guid.NewGuid().ToString(),
                    DeviceId = device.Id,
                    AppId = appId,
                    Date = bucket.Start,
                    Duration = bucket.Duration
                };
                dbContext.Usages.Add(usage);
                byKey[(appId, bucket.Start)] = usage;
            }
        }

        device.LastSeen = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync();

        var missingIcons = apps.Values.Where(a => !a.HasIcon).Select(a => a.Identifier).ToList();
        return new UsageUploadResultDto(buckets.Count, missingIcons);
    }

    private sealed record AppRef(int Id, string Identifier, bool HasIcon);

    private async Task<Dictionary<string, AppRef>> GetOrCreateAppsAsync(List<Bucket> buckets)
    {
        // Name and category per app; any entry that reports a category counts, regardless of order.
        var wanted = buckets
            .GroupBy(b => b.Identifier)
            .ToDictionary(g => g.Key, g => (Name: g.Last().AppName, Category: g.LastOrDefault(b => b.Category != null)?.Category));
        var identifiers = wanted.Keys.ToList();

        var apps = await dbContext.RegisteredApps
            .Where(a => a.Identifier != null && identifiers.Contains(a.Identifier))
            .Select(a => new { a.Id, a.Identifier, a.CategoryId, HasIcon = a.IconBitmap != null })
            .ToListAsync();

        var result = apps.ToDictionary(a => a.Identifier!, a => new AppRef(a.Id, a.Identifier!, a.HasIcon));

        var missing = identifiers.Where(i => !result.ContainsKey(i)).ToList();
        var uncategorized = apps.Where(a => a.CategoryId == null && wanted[a.Identifier!].Category != null).ToList();
        if (missing.Count == 0 && uncategorized.Count == 0)
            return result;

        var categories = await GetOrCreateCategoriesAsync(
            missing.Concat(uncategorized.Select(a => a.Identifier!)).Select(i => wanted[i].Category));

        // Existing apps that were first seen without a category get the one reported now.
        foreach (var app in uncategorized)
        {
            var entity = new RegisteredApp { Id = app.Id };
            dbContext.RegisteredApps.Attach(entity);
            entity.Category = categories[wanted[app.Identifier!].Category!];
        }

        var created = missing.Select(identifier => new RegisteredApp
        {
            Identifier = identifier,
            Name = wanted[identifier].Name,
            Category = wanted[identifier].Category is { } category ? categories[category] : null
        }).ToList();

        dbContext.RegisteredApps.AddRange(created);
        await dbContext.SaveChangesAsync();

        foreach (var app in created)
            result[app.Identifier!] = new AppRef(app.Id, app.Identifier!, false);

        return result;
    }

    private async Task<Dictionary<string, Category>> GetOrCreateCategoriesAsync(IEnumerable<string?> names)
    {
        var wanted = names.OfType<string>().Distinct().ToList();

        var result = await dbContext.Categories
            .Where(c => c.Name != null && wanted.Contains(c.Name))
            .ToDictionaryAsync(c => c.Name!);

        foreach (var name in wanted.Where(n => !result.ContainsKey(n)))
        {
            var category = new Category { Name = name };
            dbContext.Categories.Add(category);
            result[name] = category;
        }

        return result;
    }

    private sealed record Bucket(string Identifier, string AppName, string? Category, DateTime Start, TimeSpan Duration);

    private List<Bucket> Validate(IReadOnlyList<UsageEntryDto> entries)
    {
        if (entries.Count > MaxEntriesPerUpload)
            throw new ArgumentException($"At most {MaxEntriesPerUpload} entries per upload.");

        var latestAllowed = timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        var buckets = new Dictionary<(string, DateTime), Bucket>();

        foreach (var entry in entries)
        {
            var identifier = entry.AppIdentifier?.Trim();
            if (string.IsNullOrEmpty(identifier) || identifier.Length > MaxIdentifierLength)
                throw new ArgumentException($"AppIdentifier must be 1-{MaxIdentifierLength} characters.");

            var start = ToUtc(entry.BucketStart);
            if (start.Ticks % TimeSpan.TicksPerHour != 0)
                throw new ArgumentException($"BucketStart {entry.BucketStart:O} is not the start of an hour.");
            if (start > latestAllowed)
                throw new ArgumentException($"BucketStart {entry.BucketStart:O} is in the future.");

            if (entry.ForegroundTime <= TimeSpan.Zero || entry.ForegroundTime > TimeSpan.FromHours(1))
                throw new ArgumentException("ForegroundTime must be greater than zero and at most one hour.");

            var name = string.IsNullOrWhiteSpace(entry.AppName) ? identifier : entry.AppName.Trim();
            var category = string.IsNullOrWhiteSpace(entry.Category) ? null : Truncate(entry.Category.Trim(), MaxCategoryLength);

            var bucket = new Bucket(identifier, Truncate(name, MaxAppNameLength), category, start, entry.ForegroundTime);

            // Duplicates within one upload: keep the larger value, which is the more complete measurement.
            if (!buckets.TryGetValue((identifier, start), out var other) || other.Duration < bucket.Duration)
                buckets[(identifier, start)] = bucket;
        }

        return buckets.Values.ToList();
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    /// <summary>Unspecified times are taken as UTC; the API contract is UTC throughout.</summary>
    private static DateTime ToUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Local => time.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        _ => time
    };
}
