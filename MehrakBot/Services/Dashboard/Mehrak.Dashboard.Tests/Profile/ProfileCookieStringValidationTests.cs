using System.ComponentModel.DataAnnotations;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.GameApi.Shared;

namespace Mehrak.Dashboard.Tests.Profile;

/// <summary>
/// CookieString is an additive optional input: legacy LtUid/LToken requests
/// keep working unchanged, cookie-only requests validate, and supplying both
/// is rejected rather than resolved by silent precedence. </summary>
[TestFixture]
public class ProfileCookieStringValidationTests
{
    private const string ValidToken = "v2_abcDEF123-_.=~";
    private const string ValidPassphrase = "valid-passphrase-12";
    private const string ValidCookie = "ltoken_v2=v2_abcDEF123-_.=~; ltuid_v2=123456789; extra=1";

    private static IList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Test]
    public void AddProfileRequest_LegacyInput_StillPassesValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = ValidToken,
            Passphrase = ValidPassphrase
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void AddProfileRequest_LegacyValidation_StillRejectsBadToken()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = "C4N4RY TOKEN",
            Passphrase = ValidPassphrase
        });

        Assert.That(results, Is.Not.Empty);
    }

    [Test]
    public void AddProfileRequest_CookieOnly_PassesValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            CookieString = ValidCookie,
            Passphrase = ValidPassphrase
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void AddProfileRequest_CookiePlusLToken_RejectedAsMixedInput()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 0,
            LToken = ValidToken,
            CookieString = ValidCookie,
            Passphrase = ValidPassphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.CookieString))), Is.True);
    }

    [Test]
    public void AddProfileRequest_CookiePlusLtUid_RejectedAsMixedInput()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            CookieString = ValidCookie,
            Passphrase = ValidPassphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.CookieString))), Is.True);
    }

    [Test]
    public void AddProfileRequest_NeitherCookieNorLegacy_FailsValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            Passphrase = ValidPassphrase
        });

        Assert.Multiple(() =>
        {
            Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.LtUid))), Is.True);
            Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.LToken))), Is.True);
        });
    }

    [Test]
    public void AddProfileRequest_EmptyCookieString_FallsBackToLegacy()
    {
        var results = Validate(new AddProfileRequest
        {
            LtUid = 100,
            LToken = ValidToken,
            CookieString = "",
            Passphrase = ValidPassphrase
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void AddProfileRequest_CookieStillRequiresStrongPassphrase()
    {
        var results = Validate(new AddProfileRequest
        {
            CookieString = ValidCookie,
            Passphrase = "short"
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.Passphrase))), Is.True);
    }

    [Test]
    public void AddProfileRequest_OverlongCookieString_FailsValidation()
    {
        var results = Validate(new AddProfileRequest
        {
            CookieString = new string('a', CookieCredentialParser.MaxCookieStringLength + 1),
            Passphrase = ValidPassphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(AddProfileRequest.CookieString))), Is.True);
    }

    [Test]
    public void UpdateProfileRequest_LegacyInput_StillPassesValidation()
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = ValidToken,
            Passphrase = ValidPassphrase
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void UpdateProfileRequest_CookieOnly_PassesValidation()
    {
        var results = Validate(new UpdateProfileRequest
        {
            CookieString = ValidCookie,
            Passphrase = ValidPassphrase
        });

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void UpdateProfileRequest_CookiePlusLToken_RejectedAsMixedInput()
    {
        var results = Validate(new UpdateProfileRequest
        {
            LToken = ValidToken,
            CookieString = ValidCookie,
            Passphrase = ValidPassphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(UpdateProfileRequest.CookieString))), Is.True);
    }

    [Test]
    public void UpdateProfileRequest_NeitherCookieNorLegacy_FailsValidation()
    {
        var results = Validate(new UpdateProfileRequest
        {
            Passphrase = ValidPassphrase
        });

        Assert.That(results.Any(r => r.MemberNames.Contains(nameof(UpdateProfileRequest.LToken))), Is.True);
    }
}
