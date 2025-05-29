namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("RegisteredApps")]
public class RegisteredApp
{
    [Column("Id"), Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("Name"), Required, StringLength(50)]
    public string? Name { get; set; }
    
    public byte[]? IconBitmap { get; set; }

    public List<Usage>? Usages { get; set; }
}