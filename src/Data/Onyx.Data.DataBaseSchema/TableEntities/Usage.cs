namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Usage")]
public class Usage
{
    [Column("Id"), Required, Key, StringLength(50)]
    public string? Id { get; set; }

    [Column("Date"), Required, DataType(DataType.DateTime)]
    public DateTime Date { get; set; }

    [Column("Duration"), Required, DataType(DataType.Time)]
    public TimeSpan Duration { get; set; }

    public Device? Device { get; set; }
    [Column("DeviceId"), Required] public int DeviceId { get; set; }
    public Category? Category { get; set; }
    [Column("CategoryId"), Required] public int CategoryId { get; set; }
    [Column("AppId"), Required] public int AppId { get; set; }
    public RegisteredApp? App { get; set; }
}