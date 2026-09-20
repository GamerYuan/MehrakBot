namespace Mehrak.GameApi.Shared;

/// <summary>
/// Parses a full pasted browser cookie string into the HoYoLAB credential pair
/// (<c>ltoken_v2</c> + <c>ltuid_v2</c>) used for profile authentication.
/// Callers must run pasted cookie input through this parser first and only ever
/// forward the extracted values: the raw string may contain unrelated cookies
/// and must never be logged, returned in errors, encrypted, or sent upstream. </summary>
public static class CookieCredentialParser
{
    /// <summary>
    /// Upper bound for an accepted full cookie string. Real browser cookie
    /// strings are a few kilobytes; Discord modal inputs are capped at 4000
    /// characters, so this bound comfortably covers any Discord paste while
    /// still protecting the Dashboard endpoint (which has no platform cap)
    /// from unreasonable payloads.
    /// </summary>
    public const int MaxCookieStringLength = 16 * 1024;

    private const string LTokenKey = "ltoken_v2";
    private const string LtUidKey = "ltuid_v2";

    /// <summary>
    /// Tries to extract the credential pair from a full cookie string.
    /// Cookie names are matched exactly and case-sensitively; each pair is
    /// split at the first '=', ASCII spaces around names/values are trimmed,
    /// unrelated pairs are ignored, and empty segments are skipped.
    /// Fails when the input is over the length bound, contains control
    /// characters (CR/LF/TAB/...) rather than trimmable spaces, has either key
    /// missing/empty/duplicated (even with identical values), or carries an
    /// invalid token (see <see cref="LTokenValidator"/>) or UID. The UID must
    /// be positive decimal digits fitting <see cref="long.MaxValue"/> because
    /// persistence casts it to <c>long</c>. A "Cookie:" header prefix and
    /// quoted values are intentionally not supported: they fail closed as
    /// missing credentials. On failure the outputs are cleared so hostile
    /// input never flows back to callers; failure reasons are deliberately
    /// boolean-only so error paths stay generic.
    /// </summary>
    public static bool TryParse(string? cookieString, out ulong ltUid, out string ltoken)
    {
        ltUid = 0;
        ltoken = string.Empty;

        if (string.IsNullOrEmpty(cookieString) || cookieString.Length > MaxCookieStringLength)
            return false;

        // Reject control characters outright instead of stripping them: only
        // ASCII spaces are treated as trimmable separator whitespace below.
        foreach (var c in cookieString)
        {
            if (c < 0x20 || c == 0x7F)
                return false;
        }

        string? rawLToken = null;
        string? rawLtUid = null;

        foreach (var segment in cookieString.Split(';'))
        {
            var pair = segment.Trim(' ');
            if (pair.Length == 0)
                continue;

            var equals = pair.IndexOf('=');
            if (equals < 0)
                continue;

            var name = pair.Substring(0, equals).Trim(' ');
            var value = pair.Substring(equals + 1).Trim(' ');

            if (name.Equals(LTokenKey, StringComparison.Ordinal))
            {
                if (rawLToken is not null)
                    return false;
                rawLToken = value;
            }
            else if (name.Equals(LtUidKey, StringComparison.Ordinal))
            {
                if (rawLtUid is not null)
                    return false;
                rawLtUid = value;
            }
            // Unrelated cookie pairs are ignored.
        }

        if (string.IsNullOrEmpty(rawLToken) || string.IsNullOrEmpty(rawLtUid))
            return false;

        if (!LTokenValidator.IsValidLToken(rawLToken))
            return false;

        if (!TryParseLtUid(rawLtUid, out ltUid))
            return false;

        ltoken = rawLToken;
        return true;
    }

    private static bool TryParseLtUid(string value, out ulong ltUid)
    {
        ltUid = 0;

        if (value.Length == 0 || value.Length > 19)
            return false;

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
                return false;
        }

        // Digits-only at this point. Parse as long (not ulong) because the
        // credential is persisted via a (long) cast, and require positive.
        if (!long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var signed) || signed <= 0)
            return false;

        ltUid = (ulong)signed;
        return true;
    }
}
