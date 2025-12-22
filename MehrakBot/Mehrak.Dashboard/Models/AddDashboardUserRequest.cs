using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models
{
    public class AddDashboardUserRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long DiscordUserId { get; set; }

        public bool IsSuperAdmin { get; set; }

        public IEnumerable<string> GameWritePermissions { get; set; } = Array.Empty<string>();
    }
}
