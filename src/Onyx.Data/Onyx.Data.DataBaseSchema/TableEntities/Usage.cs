using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Mime;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table ("Usage")]
public class Usage
{
    public string Id { get; set; }
    public DateOnly Date { get; set; }
    public TimeSpan Duration { get; set; }
    public Devices Devices { get; set; }
    public int DeviceId { get; set; }
    public Category Category { get; set; }
    public int CategoryId { get; set; }
    public int AppId { get; set; }
    public RegisteredApp App { get; set; }
}