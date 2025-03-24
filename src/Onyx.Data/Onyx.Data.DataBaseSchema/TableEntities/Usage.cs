using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Mime;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table ("Usage")]
public class Usage
{
    public string Id { get; set; }
    public DateOnly date { get; set; }
    public TimeSpan duration { get; set; }
    public List<Devices> devices { get; set; }
    public int deviceId { get; set; }
    public List<Category> category { get; set; }
    public int categoryId { get; set; }
    public List<RegisteredApp> app { get; set; }
}