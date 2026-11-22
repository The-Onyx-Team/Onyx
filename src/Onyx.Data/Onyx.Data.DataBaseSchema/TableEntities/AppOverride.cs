using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

/// <summary>
/// A user's own name and/or category for an app, e.g. for packages the device could not translate to a name.
/// Apps are shared between users, so the reported name stays untouched and this only changes what the user sees.
/// </summary>
[Table("AppOverrides")]
public class AppOverride
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity), Column("Id")]
    public int Id { get; set; }

    [Column("UserId"), Required, StringLength(450)] // same length as AspNetUsers.Id (SQL Server requires it)
    public string? UserId { get; set; }

    public ApplicationUser? User { get; set; }

    [Column("AppId")]
    public int AppId { get; set; }

    public RegisteredApp? App { get; set; }

    /// <summary>Shown instead of the reported name; null keeps the reported name.</summary>
    [Column("DisplayName"), StringLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>Shown instead of the reported category; null keeps the reported category.</summary>
    [Column("Category"), StringLength(500)]
    public string? Category { get; set; }
}
