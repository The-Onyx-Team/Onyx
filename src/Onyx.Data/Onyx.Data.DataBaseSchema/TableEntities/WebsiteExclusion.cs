using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

/// <summary>A website (and its subdomains) the user never wants recorded. Its time stays with the browser.</summary>
[Table("WebsiteExclusions")]
public class WebsiteExclusion
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity), Column("Id")]
    public int Id { get; set; }

    [Column("UserId"), Required, StringLength(450)] // same length as AspNetUsers.Id (SQL Server requires it)
    public string? UserId { get; set; }

    public ApplicationUser? User { get; set; }

    [Column("Domain"), Required, StringLength(253)]
    public string Domain { get; set; } = "";
}
