using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Devices")]
public class Device 
{
    [Key,DatabaseGenerated(DatabaseGeneratedOption.Identity),Column("Id")]
    public int Id { get; set; }
    [Column("Name",TypeName = "VARCHAR(500)"),Required]
    public string? Name { get; set; }
    public ApplicationUser? User { get; set; }
    [Column("UserId",TypeName = "VARCHAR(50)"),Required]
    public string? UserId { get; set; }
    public List<Usage>? Usages { get; set; }
}