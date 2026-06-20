using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Profile.Models;

public class AddProfileRequest
{
    [Required]
    public ulong LtUid { get; set; }

    [Required]
    public string LToken { get; set; } = "";

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Passphrase { get; set; } = "";
}
