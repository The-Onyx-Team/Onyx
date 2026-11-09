using Microsoft.EntityFrameworkCore;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.Web.Services.Auth;
using Onyx.Data.ApiSchema;
using Onyx.Data.DataBaseSchema;

namespace Onyx.App.Web.Services.Data;

public class AppManager(ApplicationDbContext dbContext, CurrentUser currentUser) : IAppManager
{
    public const int MaxIconBytes = 256 * 1024;

    private static readonly byte[] s_PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public async Task<List<AppDto>> GetAppsAsync()
    {
        var userId = await currentUser.GetRequiredUserIdAsync();

        // Apps are shared between users; only list the ones this user has data for.
        return await dbContext.RegisteredApps
            .Where(a => a.Usages!.Any(u => u.Device!.UserId == userId))
            .Select(a => new AppDto
            {
                Id = a.Id,
                Identifier = a.Identifier,
                Name = a.Name!,
                Category = a.Category != null ? a.Category.Name : null,
                IconBitmap = a.IconBitmap ?? Array.Empty<byte>()
            })
            .ToListAsync();
    }

    public async Task<bool> SetIconAsync(string appIdentifier, byte[] iconPng)
    {
        var userId = await currentUser.GetRequiredUserIdAsync();

        if (iconPng.Length is 0 or > MaxIconBytes)
            throw new ArgumentException($"Icon must be 1 to {MaxIconBytes} bytes.");
        if (!iconPng.AsSpan().StartsWith(s_PngSignature))
            throw new ArgumentException("Icon must be a PNG image.");

        // Only a user who actually uploaded usage for the app may provide its (shared) icon.
        var app = await dbContext.RegisteredApps.FirstOrDefaultAsync(a =>
            a.Identifier == appIdentifier && a.Usages!.Any(u => u.Device!.UserId == userId));
        if (app is null || app.IconBitmap is not null)
            return false;

        app.IconBitmap = iconPng;
        await dbContext.SaveChangesAsync();
        return true;
    }
}
