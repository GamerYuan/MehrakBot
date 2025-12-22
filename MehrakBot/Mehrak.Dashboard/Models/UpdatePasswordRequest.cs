using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class UpdatePasswordRequest
{
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "New password must be at least 12 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
