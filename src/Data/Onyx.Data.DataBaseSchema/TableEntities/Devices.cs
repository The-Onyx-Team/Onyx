using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Devices")]
public class Device
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity), Column("Id")]
    public int Id { get; set; }

    [Column("Name"), Required, StringLength(500)]
    public string? Name { get; set; }

    public ApplicationUser? User { get; set; }

    [Column("UserId"), Required, StringLength(50)]
    public string? UserId { get; set; }

    public List<Usage>? Usages { get; set; }
}