using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class ResetPasswordRequest
{
    [Required]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "New password must be at least 12 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
