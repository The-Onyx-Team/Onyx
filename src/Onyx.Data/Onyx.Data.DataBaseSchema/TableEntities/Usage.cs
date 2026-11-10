using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Usage")]
public class Usage
{
    [Column("Id"), Required, Key, StringLength(50)]
    public string? Id { get; set; }

    /// <summary>Start of the hourly UTC bucket this row covers.</summary>
    [Column("Date"), Required, DataType(DataType.DateTime)]
    public DateTime Date { get; set; }

    /// <summary>Foreground time of the app within the bucket (at most one hour).</summary>
    [Column("Duration"), Required, DataType(DataType.Time)]
    public TimeSpan Duration { get; set; }

    /// <summary>Website the time was spent on (browsers on devices that record websites), as normalized by
    /// <c>WebDomain</c>. Empty for everything else; not null, because SQLite treats NULLs in a unique index as
    /// distinct and re-uploads would then add rows instead of replacing them.</summary>
    [Column("Domain"), Required, StringLength(253)]
    public string Domain { get; set; } = "";

    public Device? Device { get; set; }
    [Column("DeviceId"), Required] public int DeviceId { get; set; }
    public Category? Category { get; set; }
    [Column("CategoryId")] public int? CategoryId { get; set; }
    [Column("AppId"), Required] public int AppId { get; set; }
    public RegisteredApp? App { get; set; }
}