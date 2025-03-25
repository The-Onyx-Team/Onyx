using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Devices")]
public class Devices
{
    [Key,DatabaseGenerated(DatabaseGeneratedOption.Identity),Column("Id")]
    public int Id { get; set; }
    [Column("Name"),Required]
    public string Name { get; set; }
    public ApplicationUser Users { get; set; }
    [Column("UserId"),Required]
    public int UserId { get; set; }
}