using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class LoginRequest
{
    [Required, MinLength(1)]
    public string Username { get; set; } = default!;

    [Required, MinLength(8)]
    public string Password { get; set; } = default!;
}
