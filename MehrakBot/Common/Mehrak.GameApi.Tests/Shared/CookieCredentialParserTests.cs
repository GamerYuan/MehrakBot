using Mehrak.GameApi.Shared;

namespace Mehrak.GameApi.Tests.Shared;

/// <summary>
/// A pasted browser cookie string must yield exactly the ltoken_v2/ltuid_v2
/// pair (or a clean boolean failure): reordered pairs, separator spaces,
/// unrelated cookies, and '=' inside the token are accepted, while
/// missing/duplicate/inexact keys, malformed tokens, bad UIDs, control
/// characters, and oversized input fail without throwing or echoing the
/// hostile value back through the outputs. </summary>
[TestFixture]
public class CookieCredentialParserTests
{
    private const string ValidToken = "v2_abcDEF123-_.=~";
    private const ulong ValidUid = 123456789UL;

    private static string Cookie(string ltoken = ValidToken, string ltuid = "123456789") =>
        $"ltoken_v2={ltoken}; ltuid_v2={ltuid}";

    [Test]
    public void TryParse_ExactPair_ReturnsExtractedValues()
    {
        Assert.That(CookieCredentialParser.TryParse(Cookie(), out var ltUid, out var ltoken), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(ValidUid));
            Assert.That(ltoken, Is.EqualTo(ValidToken));
        });
    }

    [Test]
    public void TryParse_ReorderedPairs_ReturnsExtractedValues()
    {
        Assert.That(CookieCredentialParser.TryParse(
            $"ltuid_v2={ValidUid}; ltoken_v2={ValidToken}", out var ltUid, out var ltoken), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(ValidUid));
            Assert.That(ltoken, Is.EqualTo(ValidToken));
        });
    }

    [Test]
    public void TryParse_SeparatorWhitespace_ReturnsExtractedValues()
    {
        Assert.That(CookieCredentialParser.TryParse(
            $"  ltoken_v2  =  {ValidToken}  ;  ltuid_v2  =  {ValidUid}  ;  ",
            out var ltUid, out var ltoken), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(ValidUid));
            Assert.That(ltoken, Is.EqualTo(ValidToken));
        });
    }

    [Test]
    public void TryParse_ExtraCookies_IgnoresUnrelatedPairs()
    {
        Assert.That(CookieCredentialParser.TryParse(
            $"other=xyz;ltoken_v2={ValidToken};session=abc123;ltuid_v2={ValidUid};theme=dark",
            out var ltUid, out var ltoken), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(ValidUid));
            Assert.That(ltoken, Is.EqualTo(ValidToken));
        });
    }

    [Test]
    public void TryParse_TokenContainingEquals_SplitsAtFirstEquals()
    {
        const string tokenWithEquals = "v2_token==with=equals";
        Assert.That(CookieCredentialParser.TryParse(Cookie(tokenWithEquals), out _, out var ltoken), Is.True);
        Assert.That(ltoken, Is.EqualTo(tokenWithEquals));
    }

    [Test]
    public void TryParse_DuplicateUnrelatedKey_StillSucceeds()
    {
        Assert.That(CookieCredentialParser.TryParse(
            $"other=1;ltoken_v2={ValidToken};other=1;ltuid_v2={ValidUid}",
            out var ltUid, out var ltoken), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(ValidUid));
            Assert.That(ltoken, Is.EqualTo(ValidToken));
        });
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("ltoken_v2=v2_abcDEF123")]
    [TestCase("ltuid_v2=123456789")]
    [TestCase("other=xyz; session=abc")]
    [TestCase("ltoken_v2=; ltuid_v2=123456789", Description = "Empty token")]
    [TestCase("ltoken_v2=v2_abcDEF123; ltuid_v2=", Description = "Empty UID")]
    [TestCase("ltoken_v2=v2_abcDEF123; ltuid_v2=   ", Description = "Blank UID")]
    public void TryParse_MissingOrEmptyKey_ReturnsFalse(string? cookie)
    {
        Assert.That(CookieCredentialParser.TryParse(cookie, out var ltUid, out var ltoken), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(0));
            Assert.That(ltoken, Is.Empty);
        });
    }

    [Test]
    [TestCase("ltoken_v2=v2_abc; ltoken_v2=v2_abc; ltuid_v2=123", Description = "Identical duplicates still rejected")]
    [TestCase("ltoken_v2=v2_aaa; ltoken_v2=v2_bbb; ltuid_v2=123")]
    [TestCase("ltoken_v2=v2_abc; ltuid_v2=123; ltuid_v2=123", Description = "Identical UID duplicates still rejected")]
    [TestCase("ltoken_v2=v2_abc; ltuid_v2=123; ltuid_v2=456")]
    public void TryParse_DuplicateKey_ReturnsFalse(string cookie)
    {
        Assert.That(CookieCredentialParser.TryParse(cookie, out _, out _), Is.False);
    }

    [Test]
    [TestCase("LTOKEN_V2=v2_abcDEF123; ltuid_v2=123456789", Description = "Cookie names are case sensitive")]
    [TestCase("ltoken_V2=v2_abcDEF123; ltuid_v2=123456789")]
    [TestCase("ltoken_v2=v2_abcDEF123; LTUID_V2=123456789")]
    [TestCase("xltoken_v2=v2_abcDEF123; ltuid_v2=123456789", Description = "Prefix must not match")]
    [TestCase("ltoken_v2x=v2_abcDEF123; ltuid_v2=123456789", Description = "Suffix must not match")]
    [TestCase("Cookie: ltoken_v2=v2_abcDEF123; ltuid_v2=123456789", Description = "Header prefixes unsupported")]
    [TestCase("ltoken_v2=\"v2_abcDEF123\"; ltuid_v2=123456789", Description = "Quoted tokens unsupported")]
    public void TryParse_InexactKeyMatching_ReturnsFalse(string cookie)
    {
        Assert.That(CookieCredentialParser.TryParse(cookie, out _, out _), Is.False);
    }

    [Test]
    [TestCase("C4N4RY TOKEN", Description = "Whitespace is not a cookie-octet")]
    // NB: ';' is the pair separator, so it can never appear inside a token
    // value; it structurally splits the cookie instead.
    [TestCase("C4N4RY,TOKEN")]
    [TestCase("C4N4RY\"TOKEN")]
    public void TryParse_InvalidToken_ReturnsFalse(string token)
    {
        Assert.That(CookieCredentialParser.TryParse(Cookie(token), out _, out _), Is.False);
    }

    [Test]
    [TestCase("0", Description = "UID must be positive")]
    [TestCase("-1")]
    [TestCase("+123")]
    [TestCase("12.5")]
    [TestCase("abc")]
    [TestCase("12a34")]
    [TestCase("9223372036854775808", Description = "long.MaxValue + 1 overflows the persistence cast")]
    [TestCase("18446744073709551615", Description = "ulong.MaxValue still overflows the persistence cast")]
    [TestCase("99999999999999999999", Description = "20 digits cannot fit")]
    public void TryParse_InvalidOrOverflowingUid_ReturnsFalse(string uid)
    {
        Assert.That(CookieCredentialParser.TryParse(Cookie(ltuid: uid), out _, out _), Is.False);
    }

    [Test]
    public void TryParse_MaxLongUid_ReturnsTrue()
    {
        Assert.That(CookieCredentialParser.TryParse(
            Cookie(ltuid: "9223372036854775807"), out var ltUid, out _), Is.True);
        Assert.That(ltUid, Is.EqualTo(long.MaxValue));
    }

    [Test]
    [TestCase("ltoken_v2=C4N4RY\x01TOKEN; ltuid_v2=123", Description = "Control character: the audit's confirmed leak trigger")]
    [TestCase("ltoken_v2=C4N4RY\nTOKEN; ltuid_v2=123")]
    [TestCase("ltoken_v2=C4N4RY\rTOKEN; ltuid_v2=123")]
    [TestCase("ltoken_v2=C4N4RY\tTOKEN; ltuid_v2=123", Description = "Tabs are rejected, not trimmed")]
    [TestCase("ltoken_v2=C4N4RY\x7FTOKEN; ltuid_v2=123", Description = "DEL")]
    [TestCase("ltoken_v2=v2_abc;\r\nltuid_v2=123", Description = "CRLF between pairs")]
    public void TryParse_ControlCharacters_ReturnsFalseWithClearedOutputs(string cookie)
    {
        Assert.That(CookieCredentialParser.TryParse(cookie, out var ltUid, out var ltoken), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(0));
            Assert.That(ltoken, Is.Empty);
        });
    }

    [Test]
    public void TryParse_OversizedInput_ReturnsFalse()
    {
        var oversized = "ltoken_v2=" + new string('a', CookieCredentialParser.MaxCookieStringLength);
        Assert.That(oversized.Length, Is.GreaterThan(CookieCredentialParser.MaxCookieStringLength));
        Assert.That(CookieCredentialParser.TryParse(oversized, out _, out _), Is.False);
    }

    [Test]
    public void TryParse_DiscordSizedCookie_ParsesSuccessfully()
    {
        // Discord modal inputs cap at 4000 characters; the parser bound must
        // comfortably cover such pastes.
        var bigToken = "v2_" + new string('a', 3500);
        var cookie = $"ltoken_v2={bigToken}; ltuid_v2={ValidUid}; extra=" + new string('b', 200);
        Assert.That(cookie.Length, Is.LessThanOrEqualTo(4000));
        Assert.That(CookieCredentialParser.TryParse(cookie, out var ltUid, out var ltoken), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(ValidUid));
            Assert.That(ltoken, Is.EqualTo(bigToken));
        });
    }

    [Test]
    public void TryParse_HostileInput_NeverThrowsOrEchoes()
    {
        const string hostile = "ltoken_v2=C4N4RY\x01X; ltuid_v2=1; Cookie: \"injected\"";
        Assert.That(() => CookieCredentialParser.TryParse(hostile, out var ltUid, out var ltoken),
            Throws.Nothing);
        Assert.That(CookieCredentialParser.TryParse(hostile, out var uid, out var token), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(uid, Is.EqualTo(0));
            Assert.That(token, Is.Empty);
        });
    }
}
