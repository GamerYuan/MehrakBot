﻿﻿using Mehrak.Bot.Shared.Modules;
using Mehrak.Domain.User.Models;
using NetCord.Rest;

namespace Mehrak.Bot.Tests.Auth;

/// <summary>
/// New/changed passphrases require a 12-character minimum in the Bot modals (client min-length plus server-side
/// validation), while the decrypt-only auth modal keeps accepting existing weak passphrases. </summary>
[TestFixture]
public class AuthModalModuleValidationTests
{
    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("x", Description = "The audit's confirmed one-character acceptance")]
    [TestCase("elevenchars")]
    public void IsValidNewPassphrase_WeakValues_ReturnsFalse(string? passphrase)
    {
        Assert.That(AuthModalModule.IsValidNewPassphrase(passphrase), Is.False);
    }

    [Test]
    [TestCase("twelvechars!")]
    [TestCase("a much longer generated multi-word passphrase")]
    [TestCase("1234567890123456789012345678901234567890123456789012345678901234", Description = "64 chars: maximum preserved")]
    public void IsValidNewPassphrase_StrongValues_ReturnsTrue(string passphrase)
    {
        Assert.That(AuthModalModule.IsValidNewPassphrase(passphrase), Is.True);
    }

    [Test]
    public void IsValidNewPassphrase_OverlongValue_ReturnsFalse()
    {
        Assert.That(AuthModalModule.IsValidNewPassphrase(new string('a', 65)), Is.False);
    }

    private static TextInputProperties FindPassphraseInput(ModalProperties modal)
    {
        return modal.Components
            .OfType<LabelProperties>()
            .Where(l => string.Equals(l.Label, "Passphrase", StringComparison.Ordinal))
            .Select(l => l.Component)
            .OfType<TextInputProperties>()
            .Single();
    }

    [Test]
    public void AddAuthModal_PassphraseInput_EnforcesLengthBounds()
    {
        var input = FindPassphraseInput(AuthModalModule.AddAuthModal);

        Assert.Multiple(() =>
        {
            Assert.That(input.MinLength, Is.EqualTo(12));
            Assert.That(input.MaxLength, Is.EqualTo(64));
        });
    }

    [Test]
    public void UpdateAuthModal_PassphraseInput_EnforcesLengthBounds()
    {
        var input = FindPassphraseInput(AuthModalModule.UpdateAuthModal(new UserProfileDto
        {
            ProfileId = 1,
            LtUid = 100
        }));

        Assert.Multiple(() =>
        {
            Assert.That(input.MinLength, Is.EqualTo(12));
            Assert.That(input.MaxLength, Is.EqualTo(64));
        });
    }

    [Test]
    public void AuthModal_PassphraseInput_HasNoMinimumForExistingWeakPassphrases()
    {
        var input = FindPassphraseInput(AuthModalModule.AuthModal("guid"));

        Assert.Multiple(() =>
        {
            Assert.That(input.MinLength, Is.Null);
            Assert.That(input.MaxLength, Is.EqualTo(64));
        });
    }
}


