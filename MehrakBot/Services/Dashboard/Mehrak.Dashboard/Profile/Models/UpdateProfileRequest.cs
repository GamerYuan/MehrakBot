﻿﻿using System.ComponentModel.DataAnnotations;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Profile.Models;

public class UpdateProfileRequest : IValidatableObject
{
    // Reject malformed credential characters/lengths before the token reaches the GameApi Cookie-header construction,
    // where illegal characters would throw a credential-embedding FormatException into retained logs.
    // Null/absent when the caller supplies CookieString instead; the conditional requirement lives in Validate below.
    [StringLength(LTokenValidator.MaxLTokenLength)]
    [RegularExpression(LTokenValidator.CookieValuePattern, ErrorMessage = "LToken contains invalid characters.")]
    public string? LToken { get; set; }

    // Additive optional full browser cookie string ("ltoken_v2=...; ltuid_v2=...; ..."). Either this or the legacy
    // LToken must be supplied, never both: mixing is rejected rather than resolved by silent precedence.
    // Detailed cookie rules are enforced by CookieCredentialParser in the controller; the cookie UID must match the
    // stored profile UID or the update is rejected before any upstream call. An empty value counts as not supplied.
    [StringLength(CookieCredentialParser.MaxCookieStringLength)]
    public string? CookieString { get; set; }

    // Changed passphrases must use a meaningful passphrase (12-64 chars). Must match AddProfileRequest and the Bot
    // add/update modals. Existing weak passphrases keep working for decryption; they are upgraded when the profile is
    // rotated through this endpoint.
    [Required]
    [StringLength(64, MinimumLength = 12)]
    public string Passphrase { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasCookie = !string.IsNullOrEmpty(CookieString);
        var hasLToken = !string.IsNullOrEmpty(LToken);

        if (hasCookie)
        {
            if (hasLToken)
                yield return new ValidationResult(
                    "Provide either CookieString or LToken, not both.",
                    [nameof(CookieString), nameof(LToken)]);
            yield break;
        }

        if (!hasLToken)
            yield return new ValidationResult("The LToken field is required.", [nameof(LToken)]);
    }
}
