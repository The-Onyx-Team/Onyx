using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.Data.DataBaseSchema;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<ApplicationUser> ApplicationUsers { get; set; }
    public DbSet<ApplicationRole> ApplicationRoles { get; set; }
    public DbSet<Groups> Groups { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Devices>Devices { get; set; }
    public DbSet<GroupHasUser> GroupHasUsers { get; set; }
    public DbSet<Usage> Usages { get; set; }
    public DbSet<RegisteredApp> RegisteredApps { get; set; }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);
        //Usage Table config
        builder.Entity<Usage>()
            .HasOne(a => a.Category)
            .WithMany()
            .HasForeignKey(a => a.CategoryId);

        builder.Entity<Usage>()
            .HasOne(a => a.App)
            .WithMany()
            .HasForeignKey(a => a.AppId);
        
        builder.Entity<Usage>()
            .HasOne(a => a.Devices)
            .WithMany()
            .HasForeignKey(a => a.DeviceId);
        
        //Groups Table config
        builder.Entity<Groups>()
            .HasOne(a => a.Users)
            .WithMany()
            .HasForeignKey(a => a.AdminId);
        
        //GroupHasUsers Table config
        builder.Entity<GroupHasUser>()
            .HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId);
        
        builder.Entity<GroupHasUser>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId);
        builder.Entity<GroupHasUser>().HasKey(a => new {a.GroupId, a.UserId});
        //Devices Table config
        builder.Entity<Devices>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId);

    }
}