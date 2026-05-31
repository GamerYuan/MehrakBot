using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Auth.Models;

public class ResetPasswordRequest
{
    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}
