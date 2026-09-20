using System.ComponentModel.DataAnnotations;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Profile.Models;

public class AddProfileRequest : IValidatableObject
{
    // [Required] is a no-op for non-nullable value types but preserves the API
    // metadata; the conditional requirement lives in Validate below.
    [Required]
    public ulong LtUid { get; set; }

    // Reject malformed credential characters/lengths before the token reaches the GameApi Cookie-header construction,
    // where illegal characters would throw a credential-embedding FormatException into retained logs.
    // Null/absent when the caller supplies CookieString instead; the conditional requirement lives in Validate below.
    [StringLength(LTokenValidator.MaxLTokenLength)]
    [RegularExpression(LTokenValidator.CookieValuePattern, ErrorMessage = "LToken contains invalid characters.")]
    public string? LToken { get; set; }

    // Additive optional full browser cookie string ("ltoken_v2=...; ltuid_v2=...; ..."). Either this or the legacy
    // LtUid/LToken pair must be supplied, never both: mixing is rejected rather than resolved by silent precedence.
    // Detailed cookie rules (exact case-sensitive keys, split at first '=', no duplicates/controls, UID fits long)
    // are enforced by CookieCredentialParser in the controller; an empty value counts as not supplied.
    [StringLength(CookieCredentialParser.MaxCookieStringLength)]
    public string? CookieString { get; set; }

    // New profiles must use a meaningful passphrase (12-64 chars). Must match UpdateProfileRequest and the Bot
    // add/update modals. Existing weak passphrases keep working for decryption; they are upgraded when the profile is
    // rotated through UpdateProfile.
    [Required]
    [StringLength(64, MinimumLength = 12)]
    public string Passphrase { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasCookie = !string.IsNullOrEmpty(CookieString);
        var hasLToken = !string.IsNullOrEmpty(LToken);
        var hasLtUid = LtUid != 0;

        if (hasCookie)
        {
            if (hasLToken || hasLtUid)
                yield return new ValidationResult(
                    "Provide either CookieString or LtUid/LToken, not both.",
                    [nameof(CookieString), nameof(LToken), nameof(LtUid)]);
            yield break;
        }

        if (!hasLtUid)
            yield return new ValidationResult("The LtUid field is required.", [nameof(LtUid)]);
        if (!hasLToken)
            yield return new ValidationResult("The LToken field is required.", [nameof(LToken)]);
    }
}
