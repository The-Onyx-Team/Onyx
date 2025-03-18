using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Devices")]
public class Devices
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    
}