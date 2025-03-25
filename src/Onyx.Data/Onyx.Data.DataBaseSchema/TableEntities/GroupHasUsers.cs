using System.ComponentModel.DataAnnotations.Schema;
using Onyx.Data.DataBaseSchema.Identity;

namespace Onyx.Data.DataBaseSchema.TableEntities;

[Table("GroupHasUsers")]
public class GroupHasUser
{
    public int UserId { get; set; }
    public List<ApplicationUser> User { get; set; }
    public int GroupId { get; set; }
    public List<Groups> Group { get; set; }

}