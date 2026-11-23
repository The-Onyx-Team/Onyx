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

    /// <summary>When the account was created (null for accounts from before this was recorded).</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>When the user agreed to the privacy policy, and to which version of it.</summary>
    public DateTime? PrivacyConsentAt { get; set; }

    [StringLength(20)]
    public string? PrivacyPolicyVersion { get; set; }
}