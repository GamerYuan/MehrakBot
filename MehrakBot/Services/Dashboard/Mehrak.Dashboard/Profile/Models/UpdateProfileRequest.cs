﻿﻿using System.ComponentModel.DataAnnotations;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Profile.Models;

public class UpdateProfileRequest
{
    // Reject malformed credential characters/lengths before the token reaches the GameApi Cookie-header construction,
    // where illegal characters would throw a credential-embedding FormatException into retained logs.
    [Required]
    [StringLength(LTokenValidator.MaxLTokenLength, MinimumLength = 1)]
    [RegularExpression(LTokenValidator.CookieValuePattern, ErrorMessage = "LToken contains invalid characters.")]
    public string LToken { get; set; } = "";

    // Changed passphrases must use a meaningful passphrase (12-64 chars). Must match AddProfileRequest and the Bot
    // add/update modals. Existing weak passphrases keep working for decryption; they are upgraded when the profile is
    // rotated through this endpoint.
    [Required]
    [StringLength(64, MinimumLength = 12)]
    public string Passphrase { get; set; } = "";
}


