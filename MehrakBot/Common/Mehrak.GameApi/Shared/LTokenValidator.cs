namespace Mehrak.GameApi.Shared;

/// <summary>
/// Finding 5: HoYoLAB credential (ltoken) format validation.
/// A pasted token containing cookie-illegal characters (control characters,
/// whitespace, ';', ',', non-ASCII, ...) makes HttpHeaders.Add("Cookie", ...)
/// throw a FormatException whose message embeds the entire credential value.
/// That exception object was then written to retained file logs and OTLP
/// telemetry by the generic catch blocks in the API services, disclosing the
/// credential to anyone with log access. Every Cookie-header call site must
/// reject malformed values through this validator and return a sanitized
/// failure before any header is constructed, and must never log the raw
/// credential-bearing exception.
/// </summary>
public static class LTokenValidator
{
    /// <summary>
    /// Upper bound for an accepted ltoken value. Real tokens are a few hundred
    /// characters; anything beyond this cannot be legitimate and risks
    /// oversized Cookie headers.
    /// </summary>
    public const int MaxLTokenLength = 4096;

    /// <summary>
    /// RFC 6265 section 4.1.1 cookie-octet alphabet, shared with the Dashboard
    /// request DTO annotations so both layers enforce the same charset.
    /// </summary>
    public const string CookieValuePattern = @"^[\x21\x23-\x2B\x2D-\x3A\x3C-\x5B\x5D-\x7E]+$";

    /// <summary>
    /// Returns true when the supplied token is non-empty, within the length
    /// bound, and composed only of cookie-safe characters.
    /// </summary>
    public static bool IsValidLToken(string? ltoken)
    {
        if (string.IsNullOrEmpty(ltoken) || ltoken.Length > MaxLTokenLength)
            return false;

        foreach (var c in ltoken)
        {
            if (!IsCookieOctet(c))
                return false;
        }

        return true;
    }

    private static bool IsCookieOctet(char c) =>
        c == '\x21' ||
        (c >= '\x23' && c <= '\x2B') ||
        (c >= '\x2D' && c <= '\x3A') ||
        (c >= '\x3C' && c <= '\x5B') ||
        (c >= '\x5D' && c <= '\x7E');
}
