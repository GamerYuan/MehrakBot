using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class UpdateDashboardUserRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^\\d+$", ErrorMessage = "DiscordUserId must be numeric.")]
    public string DiscordUserId { get; set; } = string.Empty;

    public bool IsSuperAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public IEnumerable<string> GameWritePermissions { get; set; } = Array.Empty<string>();
}
