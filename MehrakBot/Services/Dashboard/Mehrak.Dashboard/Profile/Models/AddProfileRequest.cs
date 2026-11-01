using System.ComponentModel.DataAnnotations;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Profile.Models;

public class AddProfileRequest
{
    // [Required] is a no-op for non-nullable value types; ProfileController enforces the positive/long-fitting range
    // before the persisted cast, upstream call, encryption, or save.
    [Required]
    public ulong LtUid { get; set; }

    // Reject malformed credential characters/lengths before the token reaches the GameApi Cookie-header construction,
    // where illegal characters would throw a credential-embedding FormatException into retained logs.
    [Required]
    [StringLength(LTokenValidator.MaxLTokenLength, MinimumLength = 1)]
    [RegularExpression(LTokenValidator.CookieValuePattern, ErrorMessage = "LToken contains invalid characters.")]
    public string LToken { get; set; } = "";

    // New profiles must use a meaningful passphrase (12-64 chars). Must match UpdateProfileRequest and the Bot
    // add/update modals. Existing weak passphrases keep working for decryption; they are upgraded when the profile is
    // rotated through UpdateProfile.
    [Required]
    [StringLength(64, MinimumLength = 12)]
    public string Passphrase { get; set; } = "";
}
