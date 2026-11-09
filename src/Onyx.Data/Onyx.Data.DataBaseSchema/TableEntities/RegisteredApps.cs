using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("RegisteredApps")]
public class RegisteredApp
{
    [Column("Id"), Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Stable, platform-qualified key the collectors report, e.g. <c>android:com.whatsapp</c>,
    /// <c>windows:chrome.exe</c> or <c>linux:firefox</c>. Null only for apps created before uploads used it.
    /// </summary>
    [Column("Identifier"), StringLength(200)]
    public string? Identifier { get; set; }

    [Column("Name"), Required, StringLength(200)]
    public string? Name { get; set; }
    
    /// <summary>PNG icon, uploaded once by the first client that has it.</summary>
    public byte[]? IconBitmap { get; set; }

    public Category? Category { get; set; }
    [Column("CategoryId")] public int? CategoryId { get; set; }

    public List<Usage>? Usages { get; set; }
}