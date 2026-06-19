using Mehrak.Domain.Shared.Utility;

namespace Mehrak.Domain.Tests.Shared.Utility;

[TestFixture]
public class EasingTests
{
    // Evaluate returns an alpha multiplier whose contract is 1 -> 0 as t goes 0 -> 1:
    // 1 (fully opaque) at the start of the fade, 0 (transparent) at the end.

    private const float Tolerance = 1e-5f;

    [Test]
    public void Evaluate_AtStart_ReturnsOne([Values] EasingType type)
    {
        // t = 0 must be fully opaque for every easing type.
        Assert.That(Easing.Evaluate(type, 0f), Is.EqualTo(1f).Within(Tolerance),
            $"{type} at t=0 should be 1 (opaque)");
    }

    [Test]
    public void Evaluate_AtEnd_ReturnsZero(
        [Values(EasingType.Linear, EasingType.InCubic, EasingType.OutCubic,
                EasingType.InOutCubic, EasingType.InQuint, EasingType.OutQuint,
                EasingType.InOutQuint)]
        EasingType type)
    {
        // t = 1 must be fully transparent for every fading easing type (None is excluded).
        Assert.That(Easing.Evaluate(type, 1f), Is.EqualTo(0f).Within(Tolerance),
            $"{type} at t=1 should be 0 (transparent)");
    }

    [TestCase(EasingType.None, 1f)]
    [TestCase(EasingType.None, 0.5f)]
    [TestCase(EasingType.None, 1f)]
    public void Evaluate_None_AlwaysReturnsOne(EasingType type, float t)
    {
        Assert.That(Easing.Evaluate(type, t), Is.EqualTo(1f).Within(Tolerance));
    }

    [TestCase(EasingType.Linear, 0.25f, ExpectedResult = 0.75f)]
    [TestCase(EasingType.Linear, 0.50f, ExpectedResult = 0.50f)]
    [TestCase(EasingType.Linear, 0.75f, ExpectedResult = 0.25f)]
    public float Evaluate_Linear_DecreasesFromOneToZero(EasingType type, float t)
    {
        return Easing.Evaluate(type, t);
    }

    [TestCase(EasingType.InCubic, 0.0f, ExpectedResult = 1f)]
    [TestCase(EasingType.InCubic, 0.5f, ExpectedResult = 0.125f)]
    [TestCase(EasingType.InCubic, 1.0f, ExpectedResult = 0f)]
    public float Evaluate_InCubic_FollowsOneMinusTCubed(EasingType type, float t)
    {
        return Easing.Evaluate(type, t);
    }

    [TestCase(EasingType.OutCubic, 0.0f, ExpectedResult = 1f)]
    [TestCase(EasingType.OutCubic, 0.5f, ExpectedResult = 0.875f)]
    [TestCase(EasingType.OutCubic, 1.0f, ExpectedResult = 0f)]
    public float Evaluate_OutCubic_FollowsOneMinusTCubedComplement(EasingType type, float t)
    {
        return Easing.Evaluate(type, t);
    }

    // InOut curves are mirrored Penner easeInOut (1 -> 0):
    //   first half  : 1 - 4*t^3   (resp. 1 - 16*t^5)
    //   second half : (-2t+2)^3/2 (resp. (-2t+2)^5/2)
    // They must be continuous at t=0.5 (both halves equal 0.5).

    [TestCase(EasingType.InOutCubic, 0.0f, ExpectedResult = 1f)]
    [TestCase(EasingType.InOutCubic, 0.25f, ExpectedResult = 0.9375f)] // 1 - 4*(0.25)^3
    [TestCase(EasingType.InOutCubic, 0.5f, ExpectedResult = 0.5f)]     // continuity
    [TestCase(EasingType.InOutCubic, 0.75f, ExpectedResult = 0.0625f)] // (1/2)^3 / 2
    [TestCase(EasingType.InOutCubic, 1.0f, ExpectedResult = 0f)]
    public float Evaluate_InOutCubic_FollowsMirroredPennerCurve(EasingType type, float t)
    {
        return Easing.Evaluate(type, t);
    }

    [TestCase(EasingType.InOutQuint, 0.0f, ExpectedResult = 1f)]
    [TestCase(EasingType.InOutQuint, 0.25f, ExpectedResult = 0.984375f)] // 1 - 16*(0.25)^5
    [TestCase(EasingType.InOutQuint, 0.5f, ExpectedResult = 0.5f)]     // continuity
    [TestCase(EasingType.InOutQuint, 0.75f, ExpectedResult = 0.015625f)] // (1/2)^5 / 2
    [TestCase(EasingType.InOutQuint, 1.0f, ExpectedResult = 0f)]
    public float Evaluate_InOutQuint_FollowsMirroredPennerCurve(EasingType type, float t)
    {
        return Easing.Evaluate(type, t);
    }

    [Test]
    public void Evaluate_InOutCubic_IsContinuousAndMonotonicAcrossMidpoint()
    {
        // Crossing t=0.5 should be smooth and monotonically decreasing.
        var below = Easing.Evaluate(EasingType.InOutCubic, 0.5f - 1e-4f);
        var at = Easing.Evaluate(EasingType.InOutCubic, 0.5f);
        var above = Easing.Evaluate(EasingType.InOutCubic, 0.5f + 1e-4f);

        Assert.Multiple(() =>
        {
            Assert.That(below, Is.GreaterThan(at), "should be decreasing into the midpoint");
            Assert.That(at, Is.GreaterThan(above), "should be decreasing out of the midpoint");
            Assert.That(Math.Max(Math.Abs(below - at), Math.Abs(at - above)),
                Is.LessThan(1e-3f), "no visible kink at the midpoint");
        });
    }

    [Test]
    public void Evaluate_InOutQuint_IsContinuousAndMonotonicAcrossMidpoint()
    {
        var below = Easing.Evaluate(EasingType.InOutQuint, 0.5f - 1e-4f);
        var at = Easing.Evaluate(EasingType.InOutQuint, 0.5f);
        var above = Easing.Evaluate(EasingType.InOutQuint, 0.5f + 1e-4f);

        Assert.Multiple(() =>
        {
            Assert.That(below, Is.GreaterThan(at));
            Assert.That(at, Is.GreaterThan(above));
            Assert.That(Math.Max(Math.Abs(below - at), Math.Abs(at - above)),
                Is.LessThan(1e-3f));
        });
    }

    [TestCase(EasingType.InQuint, 0.5f, ExpectedResult = 0.03125f)] // (0.5)^5
    [TestCase(EasingType.OutQuint, 0.5f, ExpectedResult = 0.96875f)] // 1 - (0.5)^5
    public float Evaluate_Quint_FollowsOneMinusTFifth(EasingType type, float t)
    {
        return Easing.Evaluate(type, t);
    }
}
