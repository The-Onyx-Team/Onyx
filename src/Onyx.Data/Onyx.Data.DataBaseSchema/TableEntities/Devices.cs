using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Devices")]
public class Device
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity), Column("Id")]
    public int Id { get; set; }

    [Column("Name"), Required, StringLength(500)]
    public string? Name { get; set; }

    /// <summary>Android, Windows, Linux, ... as reported by the collector that registered the device.</summary>
    [Column("Platform"), StringLength(50)]
    public string? Platform { get; set; }

    /// <summary>Time of the last successful upload from this device (UTC).</summary>
    [Column("LastSeen")]
    public DateTime? LastSeen { get; set; }

    public ApplicationUser? User { get; set; }

    [Column("UserId"), Required, StringLength(450)] // same length as AspNetUsers.Id (SQL Server requires it)
    public string? UserId { get; set; }

    public List<Usage>? Usages { get; set; }
}