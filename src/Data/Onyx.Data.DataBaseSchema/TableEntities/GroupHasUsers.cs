using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("GroupHasUsers")]
public class GroupHasUser
{
    [Column("UserId"), StringLength(50)] public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    [Column("GroupId")] public int GroupId { get; set; }
    public Groups? Group { get; set; }
}