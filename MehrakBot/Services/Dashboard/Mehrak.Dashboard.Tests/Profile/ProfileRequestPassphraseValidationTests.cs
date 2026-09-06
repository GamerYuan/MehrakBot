using System.ComponentModel.DataAnnotations;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.Dashboard.ProfileAuth.Models;

namespace Mehrak.Dashboard.Tests.Profile;

/// <summary>
/// Finding 11: newly created/changed passphrases must meet the 12-character
/// minimum in Bot and Dashboard alike, while unlocking EXISTING profiles with
/// a weak passphrase keeps working so users are not locked out.
/// </summary>
[TestFixture]
public class ProfileRequestPassphraseValidationTests
{
    private static IList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Test]
    [TestCase("x", Description = "The audit's confirmed one-character acceptance")]
    [TestCase("short")]
    [TestCase("elevenchars")]
    public void AddProfileRequest_WeakPassphrase_FailsValidation(string passphrase)
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = "v2_validtoken123",
            Passphrase = passphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.Passphrase))), Is.True);
    }

    [Test]
    [TestCase("twelvechars!")]
    [TestCase("a much longer generated multi-word passphrase")]
    [TestCase("1234567890123456789012345678901234567890123456789012345678901234", Description = "64 chars: maximum preserved")]
    public void AddProfileRequest_StrongPassphrase_PassesValidation(string passphrase)
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = "v2_validtoken123",
            Passphrase = passphrase
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void AddProfileRequest_OverlongPassphrase_FailsValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = "v2_validtoken123",
            Passphrase = new string('a', 65)
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.Passphrase))), Is.True);
    }

    [Test]
    [TestCase("x")]
    [TestCase("elevenchars")]
    public void UpdateProfileRequest_WeakPassphrase_FailsValidation(string passphrase)
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = "v2_validtoken123",
            Passphrase = passphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(UpdateProfileRequest.Passphrase))), Is.True);
    }

    [Test]
    public void UpdateProfileRequest_StrongPassphrase_PassesValidation()
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = "v2_validtoken123",
            Passphrase = "twelvechars!"
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    [TestCase("x", Description = "Existing weak passphrases must remain usable for decryption")]
    [TestCase("elevenchars")]
    [TestCase("twelvechars!")]
    public void ProfileAuthenticationRequest_AnyNonEmptyPassphrase_PassesValidation(string passphrase)
    {
        var results = Validate(new ProfileAuthenticationRequest
        {
            ProfileId = 1,
            Passphrase = passphrase
        });

        Assert.That(results, Is.Empty);
    }
}
