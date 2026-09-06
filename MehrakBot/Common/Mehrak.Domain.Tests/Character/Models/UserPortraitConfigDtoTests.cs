using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Character.Models;

namespace Mehrak.Domain.Tests.Character.Models;

/// <summary>
/// API-level scale validation: user-controlled TargetScale must be
/// finite and bounded. The shared card renderer additionally clamps the resulting
/// output allocation, since an in-range scale is still unsafe on a huge source.
/// </summary>
[TestFixture]
public class UserPortraitConfigDtoTests
{
    private static bool IsScaleValid(float? scale)
    {
        var dto = new UserPortraitConfigDto { TargetScale = scale };
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
    }

    [TestCase(0.01f, ExpectedResult = true)]
    [TestCase(1f, ExpectedResult = true)]
    [TestCase(10f, ExpectedResult = true)]
    [TestCase(null, ExpectedResult = true)]
    [TestCase(0f, ExpectedResult = false)]
    [TestCase(-1f, ExpectedResult = false)]
    [TestCase(10.01f, ExpectedResult = false)]
    [TestCase(100f, ExpectedResult = false)]
    [TestCase(float.NaN, ExpectedResult = false)]
    [TestCase(float.PositiveInfinity, ExpectedResult = false)]
    [TestCase(float.NegativeInfinity, ExpectedResult = false)]
    public bool TargetScale_RangeValidation(float? scale)
    {
        return IsScaleValid(scale);
    }
}
