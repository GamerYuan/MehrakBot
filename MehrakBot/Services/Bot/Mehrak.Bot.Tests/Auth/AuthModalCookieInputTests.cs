using Mehrak.Bot.Shared.Modules;
using Mehrak.Domain.User.Models;
using NetCord.Rest;

namespace Mehrak.Bot.Tests.Auth;

/// <summary>
/// Profile add/update modals take a single full cookie string plus the
/// passphrase: add has no separate UID input, update displays the stored UID
/// and rejects a cookie UID that differs from it. The shared credential
/// resolver extracts first and clears its outputs on failure so hostile input
/// never flows back toward upstream calls or encryption. </summary>
[TestFixture]
public class AuthModalCookieInputTests
{
    private static IReadOnlyList<LabelProperties> Labels(ModalProperties modal) =>
        modal.Components.OfType<LabelProperties>().ToList();

    private static TextInputProperties Input(LabelProperties label) =>
        (TextInputProperties)label.Component;

    [Test]
    public void AddAuthModal_HasSingleCookieInputPlusPassphrase()
    {
        var labels = Labels(AuthModalModule.AddAuthModal);

        Assert.That(labels.Select(l => l.Label).ToList(),
            Is.EqualTo(["HoYoLAB Cookies", "Passphrase"]));
        Assert.That(Input(labels[0]).CustomId, Is.EqualTo("ltoken"));
    }

    [Test]
    public void AddAuthModal_HasNoSeparateUidInput()
    {
        var labels = Labels(AuthModalModule.AddAuthModal);

        Assert.Multiple(() =>
        {
            Assert.That(labels.Any(l => string.Equals(l.Label, "HoYoLAB UID", StringComparison.Ordinal)), Is.False);
            Assert.That(labels.SelectMany(l => new[] { Input(l).CustomId }), Does.Not.Contain("ltuid"));
        });
    }

    [Test]
    public void UpdateAuthModal_DisplaysStoredUidWithCookieInputPlusPassphrase()
    {
        var modal = AuthModalModule.UpdateAuthModal(new UserProfileDto
        {
            ProfileId = 1,
            LtUid = 123456789UL
        });

        var display = modal.Components.OfType<TextDisplayProperties>().Single();
        Assert.That(display.Content, Does.Contain("123456789"));

        var labels = Labels(modal);
        Assert.That(labels.Select(l => l.Label).ToList(),
            Is.EqualTo(["HoYoLAB Cookies", "Passphrase"]));
        Assert.That(Input(labels[0]).CustomId, Is.EqualTo("ltoken"));
    }

    [Test]
    public void TryResolveCookieCredentials_AddPath_ReturnsExtractedValues()
    {
        var ok = AuthModalModule.TryResolveCookieCredentials(
            "other=1; ltoken_v2=extractedtok123; ltuid_v2=777",
            null, out var ltUid, out var ltoken);

        Assert.That(ok, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(777UL));
            Assert.That(ltoken, Is.EqualTo("extractedtok123"));
        });
    }

    [Test]
    public void TryResolveCookieCredentials_MatchingUid_ReturnsExtractedToken()
    {
        var ok = AuthModalModule.TryResolveCookieCredentials(
            "ltoken_v2=newtoken456; ltuid_v2=111",
            111UL, out _, out var ltoken);

        Assert.That(ok, Is.True);
        Assert.That(ltoken, Is.EqualTo("newtoken456"));
    }

    [Test]
    [TestCase("ltoken_v2=othertoken789; ltuid_v2=222", 111UL, Description = "Cookie UID differs from stored UID")]
    [TestCase("ltoken_v2=C4N4RY BAD; ltuid_v2=111", 111UL, Description = "Malformed cookie")]
    [TestCase("not-a-cookie", 111UL)]
    [TestCase("ltoken_v2=v2_abc; ltuid_v2=111; ltuid_v2=111", 111UL, Description = "Duplicates rejected even when identical")]
    [TestCase(null, 111UL)]
    [TestCase("ltoken_v2=v2_abc; ltuid_v2=9223372036854775808", null, Description = "UID overflowing the persistence cast")]
    public void TryResolveCookieCredentials_InvalidOrMismatched_ClearsOutputs(string? cookie, ulong? expected)
    {
        var ok = AuthModalModule.TryResolveCookieCredentials(cookie, expected, out var ltUid, out var ltoken);

        Assert.That(ok, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(0));
            Assert.That(ltoken, Is.Empty);
        });
    }

    [Test]
    public void TryResolveCookieCredentials_HostileInput_NeverThrowsOrEchoes()
    {
        const string hostile = "ltoken_v2=C4N4RY\x01X; ltuid_v2=111";
        Assert.That(() => AuthModalModule.TryResolveCookieCredentials(hostile, 111UL, out _, out _),
            Throws.Nothing);
        Assert.That(AuthModalModule.TryResolveCookieCredentials(hostile, 111UL, out var ltUid, out var ltoken),
            Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(ltUid, Is.EqualTo(0));
            Assert.That(ltoken, Is.Empty);
        });
    }
}
