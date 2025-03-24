using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Devices")]
public class Devices
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<ApplicationUser> users { get; set; }
    public int userId { get; set; }
}