namespace Onyx.Data.DataBaseSchema.Identity;

public class ApplicationUser : IdentityUser
{
    public List<Groups>? Groups { get; set; }
}