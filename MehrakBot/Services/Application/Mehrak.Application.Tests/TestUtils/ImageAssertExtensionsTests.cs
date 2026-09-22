using NUnit.Framework.Constraints;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Tests.TestUtils;

[TestFixture]
public sealed class ImageAssertExtensionsTests
{
    private static readonly Rgba32 Background = new(16, 20, 30, 255);
    private static readonly Rgba32 Panel = new(38, 45, 62, 245);
    private static readonly Rgba32 Accent = new(220, 30, 30, 255);
    // Bt.709 luminance is approximately equal to Accent, so luminance-only hashing cannot distinguish it.
    private static readonly Rgba32 EqualLuminanceAccent = new(30, 93, 30, 255);
    private static readonly Rgba32 Foreground = new(226, 231, 240, 255);
    private static readonly Rgba32 Material = new(245, 184, 62, 230);

    [Test]
    public void IdenticalTo_SameImageBytes_PassesDeterministically()
    {
        var expected = CreateCard();

        Assert.That(expected, IsImage.IdenticalTo(expected));
        Assert.That(expected, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_SeekableStreams_PassesAndPreservesPositions()
    {
        var bytes = CreateCard();
        using var expected = new MemoryStream(bytes);
        using var actual = new MemoryStream(bytes);
        expected.Position = 11;
        actual.Position = 17;

        var constraint = IsImage.IdenticalTo(expected);
        Assert.That(expected.Position, Is.EqualTo(11));
        Assert.That(actual, constraint);
        Assert.That(actual.Position, Is.EqualTo(17));
    }

    [Test]
    public void IdenticalTo_TwoPixelGlobalShift_Passes()
    {
        var expected = CreateCard();
        var shifted = CreateCard(globalOffsetX: 2, globalOffsetY: 1);

        Assert.That(shifted, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_TwoPixelLocalElementShift_Passes()
    {
        var expected = CreateCard();
        var shifted = CreateCard(materialOffsetX: -2, materialOffsetY: 2);

        Assert.That(shifted, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_ModerateJpegAndAntialiasVariation_Passes()
    {
        var expected = CreateCard();
        using var image = Image.Load<Rgba32>(expected);
        image.Mutate(context => context.GaussianBlur(0.45f));
        using var encoded = new MemoryStream();
        image.Save(encoded, new JpegEncoder { Quality = 82 });

        Assert.That(encoded.ToArray(), IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_TallCardList_RemainsDeterministic()
    {
        var expected = CreateCard(width: 256, height: 2048);

        Assert.That(expected, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_StrongEqualLuminanceHueChange_Fails()
    {
        var expected = CreateCard();
        var changed = CreateCard(useAlternateHue: true);

        AssertComparisonFails(changed, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_MissingMaterialIcon_Fails()
    {
        var expected = CreateCard();
        var changed = CreateCard(includeMaterialIcon: false);

        AssertComparisonFails(changed, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_ClippedLabelRegion_Fails()
    {
        var expected = CreateCard();
        var changed = CreateCard(clipLabel: true);

        AssertComparisonFails(changed, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_SubstantialDisplacement_Fails()
    {
        var expected = CreateCard();
        var changed = CreateCard(compositionOffsetX: 38);

        AssertComparisonFails(changed, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void IdenticalTo_DifferentDimensions_Fails()
    {
        var expected = CreateCard();
        var changed = CreateCard(width: 255);

        AssertComparisonFails(changed, IsImage.IdenticalTo(expected));
    }

    [Test]
    public void PerceptuallyEquivalentTo_ExplicitDctThresholdControlsGlobalHashRequirement()
    {
        var expected = CreateCard();
        var shifted = CreateCard(globalOffsetX: 2, globalOffsetY: 1);

        AssertComparisonFails(shifted, IsImage.PerceptuallyEquivalentTo(expected, 100.0));
    }

    [Test]
    public void PerceptuallyEquivalentTo_ExplicitDctThresholdIsValidated()
    {
        var expected = CreateCard();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => IsImage.PerceptuallyEquivalentTo(expected, -0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => IsImage.PerceptuallyEquivalentTo(expected, 100.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => IsImage.PerceptuallyEquivalentTo(expected, double.NaN));
        });
    }

    private static void AssertComparisonFails(byte[] actual, IResolveConstraint constraint)
    {
        var resolvedConstraint = constraint.Resolve();
        var result = resolvedConstraint.ApplyTo(actual);
        Assert.That(result.IsSuccess, Is.False,
            "The synthetic defect must remain above the comparator's documented sensitivity floor.");
    }

    private static byte[] CreateCard(int width = 256, int height = 192, int globalOffsetX = 0,
        int globalOffsetY = 0, int materialOffsetX = 0, int materialOffsetY = 0,
        int compositionOffsetX = 0, bool includeMaterialIcon = true, bool clipLabel = false,
        bool useAlternateHue = false)
    {
        using var image = new Image<Rgba32>(width, height, Background);
        var accent = useAlternateHue ? EqualLuminanceAccent : Accent;

        FillRectangle(image, 18 + globalOffsetX, 16 + globalOffsetY, 220, 158, Panel);
        FillRectangle(image, 30 + globalOffsetX + compositionOffsetX, 34 + globalOffsetY,
            92, 52, accent);
        DrawPortraitPattern(image, 38 + globalOffsetX + compositionOffsetX,
            40 + globalOffsetY, accent);

        if (includeMaterialIcon)
        {
            DrawMaterialIcon(image, 184 + globalOffsetX + materialOffsetX,
                38 + globalOffsetY + materialOffsetY);
        }

        DrawPseudoLabel(image, 36 + globalOffsetX + compositionOffsetX,
            110 + globalOffsetY, clipLabel);
        DrawStats(image, 150 + globalOffsetX, 104 + globalOffsetY);

        using var encoded = new MemoryStream();
        image.Save(encoded, new PngEncoder());
        return encoded.ToArray();
    }

    private static void DrawPortraitPattern(Image<Rgba32> image, int originX, int originY, Rgba32 accent)
    {
        FillRectangle(image, originX, originY, 18, 34, Foreground);
        FillRectangle(image, originX + 22, originY + 6, 26, 7, Foreground);
        FillRectangle(image, originX + 22, originY + 20, 34, 6, Foreground);
        FillRectangle(image, originX + 8, originY + 9, 7, 8, accent);
    }

    private static void DrawMaterialIcon(Image<Rgba32> image, int originX, int originY)
    {
        for (var y = 0; y < 28; y++)
        {
            for (var x = 0; x < 28; x++)
            {
                var distance = Math.Abs(x - 13) + Math.Abs(y - 13);
                if (distance <= 13)
                    SetPixel(image, originX + x, originY + y, Material);
                if (distance <= 6)
                    SetPixel(image, originX + x, originY + y, Foreground);
            }
        }
    }

    private static void DrawPseudoLabel(Image<Rgba32> image, int originX, int originY, bool clipLabel)
    {
        FillRectangle(image, originX, originY, 70, 5, Foreground);
        FillRectangle(image, originX, originY + 10, clipLabel ? 18 : 54, 5, Foreground);
        FillRectangle(image, originX, originY + 20, 42, 4, new Rgba32(145, 158, 184, 255));
        for (var x = 0; x < 48; x += 8)
            FillRectangle(image, originX + x, originY + 30, 4, 10, Foreground);
    }

    private static void DrawStats(Image<Rgba32> image, int originX, int originY)
    {
        for (var row = 0; row < 4; row++)
        {
            FillRectangle(image, originX, originY + row * 13, 54 - row * 6, 3, Foreground);
            FillRectangle(image, originX + 58, originY + row * 13, 12, 3, Material);
        }
    }

    private static void FillRectangle(Image<Rgba32> image, int x, int y, int width, int height,
        Rgba32 color)
    {
        for (var pixelY = Math.Max(0, y); pixelY < Math.Min(image.Height, y + height); pixelY++)
        {
            for (var pixelX = Math.Max(0, x); pixelX < Math.Min(image.Width, x + width); pixelX++)
                image[pixelX, pixelY] = color;
        }
    }

    private static void SetPixel(Image<Rgba32> image, int x, int y, Rgba32 color)
    {
        if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
            image[x, y] = color;
    }
}
