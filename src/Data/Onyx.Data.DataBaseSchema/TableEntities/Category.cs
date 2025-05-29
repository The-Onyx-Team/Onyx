namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("Categories")]
public class Category
{
    [Column("Id"), Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("Name"), Required, StringLength(500)]
    public string? Name { get; set; }

    public List<Usage>? Usages { get; set; }
}