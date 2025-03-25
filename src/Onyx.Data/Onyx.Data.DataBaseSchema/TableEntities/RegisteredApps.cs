using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("RegisteredApps")]
public class RegisteredApp
{
    public int Id { get; set; }
    public string Name { get; set; }
}