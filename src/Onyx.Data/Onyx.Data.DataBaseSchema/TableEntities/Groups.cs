using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Groups")]
public class Groups
{
    public int Id { get; set; }
    public string Name { get; set; }
}