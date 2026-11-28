using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.Data.DataBaseSchema;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<Groups> Groups { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<GroupHasUser> GroupHasUsers { get; set; }
    public DbSet<Usage> Usages { get; set; }
    public DbSet<RegisteredApp> RegisteredApps { get; set; }
    public DbSet<WebsiteExclusion> WebsiteExclusions { get; set; }
    public DbSet<AppOverride> AppOverrides { get; set; }
    public DbSet<UserVaultEntity> UserVaults { get; set; }
    public DbSet<EncryptedUsage> EncryptedUsages { get; set; }
    public DbSet<EncryptedApp> EncryptedApps { get; set; }
    public DbSet<Friendship> Friendships { get; set; }
    public DbSet<SharedStat> SharedStats { get; set; }
    public DbSet<Integration> Integrations { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);
        //Usage Table config
        builder.Entity<Usage>()
            .HasOne(a => a.Category)
            .WithMany(a => a.Usages)
            .HasForeignKey(a => a.CategoryId);

        builder.Entity<Usage>()
            .HasOne(a => a.App)
            .WithMany(a => a.Usages)
            .HasForeignKey(a => a.AppId);

        builder.Entity<Usage>()
            .HasOne(a => a.Device)
            .WithMany(a => a.Usages)
            .HasForeignKey(a => a.DeviceId);

        // One row per device, app, website and hourly bucket: re-uploading a bucket replaces it instead of adding
        // to it. Domain is "" for everything that is not a website.
        builder.Entity<Usage>()
            .HasIndex(a => new { a.DeviceId, a.AppId, a.Domain, a.Date })
            .IsUnique();

        builder.Entity<Usage>()
            .Property(a => a.Domain)
            .HasDefaultValue("");

        builder.Entity<WebsiteExclusion>()
            .HasIndex(a => new { a.UserId, a.Domain })
            .IsUnique();

        builder.Entity<WebsiteExclusion>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One override per user and app. Both parents cascade; they share no ancestor, so SQL Server accepts it.
        builder.Entity<AppOverride>()
            .HasIndex(a => new { a.UserId, a.AppId })
            .IsUnique();

        builder.Entity<AppOverride>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AppOverride>()
            .HasOne(a => a.App)
            .WithMany()
            .HasForeignKey(a => a.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        //RegisteredApps Table config
        builder.Entity<RegisteredApp>()
            .HasIndex(a => a.Identifier)
            .IsUnique();

        builder.Entity<RegisteredApp>()
            .HasOne(a => a.Category)
            .WithMany()
            .HasForeignKey(a => a.CategoryId);

        //Groups Table config
        // No cascade from the admin: users already cascade to memberships, and SQL Server rejects a second
        // cascade path. AccountDeletion removes a user's groups before deleting the user.
        builder.Entity<Groups>()
            .HasOne(a => a.Admin)
            .WithMany(a => a.Groups)
            .HasForeignKey(a => a.AdminId)
            .OnDelete(DeleteBehavior.NoAction);

        //GroupHasUsers Table config
        builder.Entity<GroupHasUser>()
            .HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId);

        builder.Entity<GroupHasUser>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId);
        builder.Entity<GroupHasUser>().HasKey(a => new { a.GroupId, a.UserId });

        // Memberships that existed before invitations count as accepted.
        builder.Entity<GroupHasUser>()
            .Property(a => a.Accepted)
            .HasDefaultValue(true);

        // End-to-end encrypted data. Rows that point at two users (friendships, shares) don't cascade: SQL Server
        // allows only one cascade path from a table, so AccountDeletion removes them before the user.
        builder.Entity<UserVaultEntity>()
            .HasOne(a => a.User)
            .WithOne()
            .HasForeignKey<UserVaultEntity>(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EncryptedUsage>()
            .HasOne(a => a.Device)
            .WithMany()
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // An upload replaces the row of an app's hour on a device.
        builder.Entity<EncryptedUsage>()
            .HasIndex(a => new { a.DeviceId, a.Hour, a.Tag })
            .IsUnique();

        builder.Entity<EncryptedApp>().HasKey(a => new { a.UserId, a.Tag });

        builder.Entity<EncryptedApp>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Friendship>()
            .HasOne(a => a.Requester)
            .WithMany()
            .HasForeignKey(a => a.RequesterId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Friendship>()
            .HasOne(a => a.Addressee)
            .WithMany()
            .HasForeignKey(a => a.AddresseeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Friendship>()
            .HasIndex(a => new { a.RequesterId, a.AddresseeId })
            .IsUnique();

        builder.Entity<SharedStat>().HasKey(a => new { a.OwnerId, a.RecipientId, a.PeriodStart });

        builder.Entity<SharedStat>()
            .HasOne(a => a.Owner)
            .WithMany()
            .HasForeignKey(a => a.OwnerId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<SharedStat>()
            .HasOne(a => a.Recipient)
            .WithMany()
            .HasForeignKey(a => a.RecipientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<SharedStat>()
            .HasIndex(a => new { a.RecipientId, a.PeriodStart });

        builder.Entity<Integration>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        //Devices Table config
        builder.Entity<Device>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId);
    }
}