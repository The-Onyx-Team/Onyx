using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Groups")]
public class Groups
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int AdminId { get; set; }
    public List<ApplicationUser> Users { get; set; }
}