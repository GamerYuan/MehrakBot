using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.User.Models;

public class AddDashboardUserRequest
{
    [Required]
    [RegularExpression("^\\d+$", ErrorMessage = "DiscordUserId must be numeric.")]
    public string DiscordUserId { get; set; } = string.Empty;

    public IEnumerable<string> GameWritePermissions { get; set; } = [];
}
