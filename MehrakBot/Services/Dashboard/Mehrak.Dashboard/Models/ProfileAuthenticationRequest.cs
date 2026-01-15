using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public sealed class ProfileAuthenticationRequest
{
    [Range(1, 10)]
    public int ProfileId { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Passphrase { get; set; } = string.Empty;
}
