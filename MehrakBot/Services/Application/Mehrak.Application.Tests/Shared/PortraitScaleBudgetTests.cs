using Mehrak.Application.Shared.Renderers;

namespace Mehrak.Application.Tests.Shared;

/// <summary>
/// Renderer allocation-budget tests: scale-derived portrait resize
/// widths must stay within the maximum dimension and pixel budget for both user
/// and stock portraits. Pure unit tests over the shared budget helper.
/// </summary>
[TestFixture]
public class PortraitScaleBudgetTests
{
    private static int Budget(int sourceWidth, int sourceHeight, float scale) =>
        CharacterCardServiceBase<object>.ComputePortraitTargetWidth(sourceWidth, sourceHeight, scale);

    [Test]
    public void OrdinaryScale_PassesThrough()
    {
        Assert.That(Budget(1000, 2000, 2f), Is.EqualTo(2000));
    }

    [Test]
    public void ExcessiveScale_ClampedToMaxDimension()
    {
        Assert.That(Budget(4096, 4096, 10f), Is.EqualTo(4096));
    }

    [Test]
    public void LargeSource_ClampedByPixelBudget()
    {
        // 1500x3000 at 10x wants 15000 wide; pixel budget allows sqrt(16.7M / 2) = 2896.
        Assert.That(Budget(1500, 3000, 10f), Is.EqualTo(2896));
    }

    [Test]
    public void TallSource_OutputStaysWithinPixelBudget()
    {
        var width = Budget(500, 4000, 10f);
        var height = (double)width * 4000 / 500;
        Assert.Multiple(() =>
        {
            Assert.That(width, Is.LessThanOrEqualTo(4096));
            Assert.That((long)width * (long)height, Is.LessThanOrEqualTo(16_777_216));
        });
    }

    [Test]
    public void TinyResult_FloorsAtOnePixel()
    {
        Assert.That(Budget(100, 100, 0.01f), Is.EqualTo(1));
    }

    [TestCase(0, 100)]
    [TestCase(100, 0)]
    [TestCase(-5, 100)]
    public void DegenerateSource_FloorsAtOnePixel(int sourceWidth, int sourceHeight)
    {
        Assert.That(Budget(sourceWidth, sourceHeight, 2f), Is.EqualTo(1));
    }
}
