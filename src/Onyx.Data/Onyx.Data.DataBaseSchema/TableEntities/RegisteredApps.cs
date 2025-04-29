using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("RegisteredApps")]
public class RegisteredApp
{
    [Column("Id"), Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("Name"), Required, StringLength(50)]
    public string? Name { get; set; }

    public List<Usage>? Usages { get; set; }
}