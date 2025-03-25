using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Groups")]
public class Groups
{
    [Key,Column("Id"),DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    [Column ("Name"),Required]
    public string Name { get; set; }
    [Column ("AdminId"),Required]
    public string AdminId { get; set; }
    public ApplicationUser Users { get; set; }
}