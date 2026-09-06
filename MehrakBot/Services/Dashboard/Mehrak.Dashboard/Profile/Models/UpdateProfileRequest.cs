using System.ComponentModel.DataAnnotations;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Profile.Models;

public class UpdateProfileRequest
{
    // Finding 5: reject malformed credential characters/lengths before the token
    // reaches the GameApi Cookie-header construction, where illegal characters
    // would throw a credential-embedding FormatException into retained logs.
    [Required]
    [StringLength(LTokenValidator.MaxLTokenLength, MinimumLength = 1)]
    [RegularExpression(LTokenValidator.CookieValuePattern, ErrorMessage = "LToken contains invalid characters.")]
    public string LToken { get; set; } = "";

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Passphrase { get; set; } = "";
}
