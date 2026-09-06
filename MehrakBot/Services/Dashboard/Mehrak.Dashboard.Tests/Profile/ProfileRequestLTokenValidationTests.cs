using System.ComponentModel.DataAnnotations;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Tests.Profile;

/// <summary>
/// Finding 5: AddProfile/UpdateProfile ltoken values must reject malformed
/// credential characters and unreasonable lengths at the API boundary, before
/// the token reaches Cookie-header construction in GameApi.
/// </summary>
[TestFixture]
public class ProfileRequestLTokenValidationTests
{
    private static IList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Test]
    public void AddProfileRequest_ValidLToken_PassesValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = "v2_abcDEF123-_.=~",
            Passphrase = "valid-passphrase-12"
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    [TestCase("C4N4RY\x01TOKEN", Description = "Control character: the audit's confirmed leak trigger")]
    [TestCase("C4N4RY;TOKEN")]
    [TestCase("C4N4RY TOKEN")]
    [TestCase("C4N4RY,TOKEN")]
    public void AddProfileRequest_MalformedLToken_FailsValidation(string ltoken)
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = ltoken,
            Passphrase = "valid-passphrase-12"
        });

        Assert.That(results, Is.Not.Empty);
    }

    [Test]
    public void AddProfileRequest_OverlongLToken_FailsValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = new string('a', LTokenValidator.MaxLTokenLength + 1),
            Passphrase = "valid-passphrase-12"
        });

        Assert.That(results, Is.Not.Empty);
    }

    [Test]
    public void UpdateProfileRequest_ValidLToken_PassesValidation()
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = "v2_abcDEF123-_.=~",
            Passphrase = "valid-passphrase-12"
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    [TestCase("C4N4RY\x01TOKEN")]
    [TestCase("C4N4RY;TOKEN")]
    public void UpdateProfileRequest_MalformedLToken_FailsValidation(string ltoken)
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = ltoken,
            Passphrase = "valid-passphrase-12"
        });

        Assert.That(results, Is.Not.Empty);
    }

    [Test]
    public void UpdateProfileRequest_OverlongLToken_FailsValidation()
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = new string('a', LTokenValidator.MaxLTokenLength + 1),
            Passphrase = "valid-passphrase-12"
        });

        Assert.That(results, Is.Not.Empty);
    }
}
