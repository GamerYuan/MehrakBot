using Mehrak.Bot.Shared.Modules;
using Mehrak.Domain.User.Models;
using NetCord.Rest;

namespace Mehrak.Bot.Tests.Auth;

/// <summary>
/// Profile add collects the HoYoLAB UID (ltuid_v2) and token (ltoken_v2) as separate inputs, and profile update shows
/// the stored UID read-only. The UID input is bounded to the persisted long range before any cast, upstream call, or
/// encryption.
/// </summary>
[TestFixture]
public class AuthModalCredentialInputTests
{
    private static IReadOnlyList<LabelProperties> Labels(ModalProperties modal) =>
        modal.Components.OfType<LabelProperties>().ToList();

    private static TextInputProperties Input(LabelProperties label) =>
        (TextInputProperties)label.Component;

    [Test]
    public void AddAuthModal_HasSeparateLabeledUidAndTokenInputs()
    {
        var labels = Labels(AuthModalModule.AddAuthModal);

        Assert.That(labels.Select(l => l.Label).ToList(),
            Is.EqualTo(["HoYoLAB UID (ltuid_v2)", "HoYoLAB ltoken_v2 Cookie", "Passphrase"]));
        Assert.Multiple(() =>
        {
            Assert.That(Input(labels[0]).CustomId, Is.EqualTo("ltuid"));
            Assert.That(Input(labels[0]).Style, Is.EqualTo(TextInputStyle.Short));
            Assert.That(Input(labels[1]).CustomId, Is.EqualTo("ltoken"));
            Assert.That(Input(labels[1]).Style, Is.EqualTo(TextInputStyle.Paragraph));
        });
    }

    [Test]
    public void UpdateAuthModal_ShowsStoredUidReadOnlyAndTokenInput()
    {
        var modal = AuthModalModule.UpdateAuthModal(new UserProfileDto
        {
            ProfileId = 1,
            LtUid = 123456789UL
        });

        var display = modal.Components.OfType<TextDisplayProperties>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(display.Content, Does.Contain("123456789"));
            Assert.That(display.Content, Does.Contain("read-only"));
        });

        var labels = Labels(modal);
        Assert.That(labels.Select(l => l.Label).ToList(),
            Is.EqualTo(["HoYoLAB ltoken_v2 Cookie", "Passphrase"]));
        Assert.Multiple(() =>
        {
            Assert.That(labels.SelectMany(l => new[] { Input(l).CustomId }), Does.Not.Contain("ltuid"));
            Assert.That(Input(labels[0]).CustomId, Is.EqualTo("ltoken"));
        });
    }

    [Test]
    public void TryParseLtUid_ValidValue_ReturnsTrue()
    {
        Assert.That(AuthModalModule.TryParseLtUid("123456789", out var ltUid), Is.True);
        Assert.That(ltUid, Is.EqualTo(123456789UL));
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-a-uid")]
    [TestCase("-1")]
    [TestCase("0", Description = "Zero would persist an unusable credential UID")]
    [TestCase("9223372036854775808", Description = "One above long.MaxValue overflows the persisted cast")]
    [TestCase("18446744073709551615", Description = "ulong.MaxValue overflows the persisted cast")]
    public void TryParseLtUid_InvalidValue_ReturnsFalseAndClearsOutput(string? value)
    {
        Assert.That(AuthModalModule.TryParseLtUid(value, out var ltUid), Is.False);
        Assert.That(ltUid, Is.EqualTo(0));
    }

    [Test]
    public void TryParseLtUid_LongMaxValue_IsAccepted()
    {
        Assert.That(AuthModalModule.TryParseLtUid(long.MaxValue.ToString(), out var ltUid), Is.True);
        Assert.That(ltUid, Is.EqualTo((ulong)long.MaxValue));
    }
}
