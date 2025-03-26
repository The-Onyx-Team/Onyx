using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace Onyx.Data.DataBaseSchema.Identity;

public class ApplicationUser : IdentityUser
{
    public List<Group> Groups { get; set; }
    
}