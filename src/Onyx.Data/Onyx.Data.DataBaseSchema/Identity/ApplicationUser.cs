using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.Data.DataBaseSchema.Identity;

public class ApplicationUser : IdentityUser
{
    public List<Groups>? Groups { get; set; }

    /// <summary>Profile picture (PNG, JPEG or WebP, at most <see cref="MaxAvatarBytes"/>); null when none is set.</summary>
    public byte[]? Avatar { get; set; }

    [StringLength(50)]
    public string? AvatarContentType { get; set; }

    public const int MaxAvatarBytes = 512 * 1024;
}