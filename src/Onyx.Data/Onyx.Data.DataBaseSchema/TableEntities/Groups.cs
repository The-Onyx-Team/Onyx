using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Groups")]
public class Groups
{
    [Key,Column("Id"),DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    [Column ("Name"),DataType(DataType.Text),Required,StringLength(500)]
    public string? Name { get; set; }
    [Column ("AdminId"),Required,StringLength(50)]
    public string? AdminId { get; set; }
    public ApplicationUser? Admin { get; set; }
}