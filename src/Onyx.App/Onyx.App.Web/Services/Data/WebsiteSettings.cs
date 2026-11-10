using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Services.Data;

public class WebsiteSettings(ApplicationDbContext dbContext, CurrentUser currentUser) : IWebsiteSettings
{
    public const int MaxExclusions = 500;

    public async Task<List<string>> GetExcludedDomainsAsync()
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        return await dbContext.WebsiteExclusions
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Domain)
            .Select(e => e.Domain)
            .ToListAsync();
    }

    public async Task<List<string>> SetExcludedDomainsAsync(IReadOnlyList<string> domains)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();

        var invalid = domains.Where(d => WebDomain.Normalize(d) is null).ToList();
        if (invalid.Count > 0)
            throw new ArgumentException($"Not a valid domain: {string.Join(", ", invalid)}");

        var wanted = domains.Select(d => WebDomain.Normalize(d)!).Distinct().Order(StringComparer.Ordinal).ToList();
        if (wanted.Count > MaxExclusions)
            throw new ArgumentException($"At most {MaxExclusions} excluded websites.");

        var existing = await dbContext.WebsiteExclusions.Where(e => e.UserId == userId).ToListAsync();
        dbContext.WebsiteExclusions.RemoveRange(existing.Where(e => !wanted.Contains(e.Domain)));

        var added = wanted.Except(existing.Select(e => e.Domain)).ToList();
        dbContext.WebsiteExclusions.AddRange(added.Select(d => new WebsiteExclusion { UserId = userId, Domain = d }));
        await dbContext.SaveChangesAsync();

        foreach (var domain in added)
            await RemoveDomainsAsync(userId, u => u.Domain == domain || u.Domain.EndsWith("." + domain));

        return wanted;
    }

    public async Task DeleteWebsiteHistoryAsync()
    {
        var userId = await currentUser.GetRequiredUserIdAsync();
        await RemoveDomainsAsync(userId, u => u.Domain != "");
    }

    /// <summary>
    /// Moves the time of matching website rows to the row without a domain for the same device, app and hour,
    /// then deletes them, so totals per app stay the same.
    /// </summary>
    private async Task RemoveDomainsAsync(string userId, System.Linq.Expressions.Expression<Func<Usage, bool>> match)
    {
        var deviceIds = await dbContext.Devices.Where(d => d.UserId == userId).Select(d => d.Id).ToListAsync();

        // One device at a time keeps the number of tracked rows bounded.
        foreach (var deviceId in deviceIds)
        {
            var websiteRows = await dbContext.Usages
                .Where(u => u.DeviceId == deviceId && u.Domain != "")
                .Where(match)
                .ToListAsync();
            if (websiteRows.Count == 0)
                continue;

            var appIds = websiteRows.Select(u => u.AppId).Distinct().ToList();
            var first = websiteRows.Min(u => u.Date);
            var last = websiteRows.Max(u => u.Date);
            var plainRows = (await dbContext.Usages
                    .Where(u => u.DeviceId == deviceId && u.Domain == "" && appIds.Contains(u.AppId)
                                && u.Date >= first && u.Date <= last)
                    .ToListAsync())
                .ToDictionary(u => (u.AppId, u.Date));

            foreach (var row in websiteRows)
            {
                if (!plainRows.TryGetValue((row.AppId, row.Date), out var plain))
                {
                    plain = new Usage
                    {
                        Id = Guid.NewGuid().ToString(),
                        DeviceId = deviceId,
                        AppId = row.AppId,
                        CategoryId = row.CategoryId,
                        Date = row.Date,
                        Duration = TimeSpan.Zero
                    };
                    dbContext.Usages.Add(plain);
                    plainRows[(row.AppId, row.Date)] = plain;
                }

                var total = plain.Duration + row.Duration;
                plain.Duration = total < TimeSpan.FromHours(1) ? total : TimeSpan.FromHours(1);
                dbContext.Usages.Remove(row);
            }

            await dbContext.SaveChangesAsync();
        }
    }
}
