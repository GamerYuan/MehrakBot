using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.ProfileAuth.Models;

public sealed class ProfileAuthenticationRequest
{
    [Range(1, 10)]
    public int ProfileId { get; set; }

    // Finding 11: this minimum intentionally stays at 1. This DTO unlocks
    // EXISTING profiles, so rejecting short values here would lock out users
    // whose passphrases predate the 12-character minimum for new/changed
    // passphrases. Those users upgrade when they rotate credentials through
    // UpdateProfile (Dashboard) or the Bot update modal.
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Passphrase { get; set; } = string.Empty;
}
