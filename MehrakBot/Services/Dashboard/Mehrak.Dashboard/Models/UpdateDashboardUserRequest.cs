using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class UpdateDashboardUserRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(20, MinimumLength = 17, ErrorMessage = "DiscordUserId must be between 17 and 20 digits.")]
    [RegularExpression("^\\d+$", ErrorMessage = "DiscordUserId must be numeric.")]
    public string DiscordUserId { get; set; } = string.Empty;

    public bool IsSuperAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public IEnumerable<string> GameWritePermissions { get; set; } = [];
}
