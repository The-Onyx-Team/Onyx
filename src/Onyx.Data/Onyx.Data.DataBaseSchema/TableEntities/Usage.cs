using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table ("Usage")]
public class Usage
{
    public string Id { get; set; }
    public DateOnly Date { get; set; }
    public TimeSpan Duration { get; set; }
}