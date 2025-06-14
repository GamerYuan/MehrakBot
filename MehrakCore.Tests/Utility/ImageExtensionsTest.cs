#region

using MehrakCore.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Tests.Utility;

[Parallelizable(ParallelScope.Fixtures | ParallelScope.Children)]
public class ImageExtensionsTest
{
    [Test]
    public void ApplyGradientFade_ShouldMakeRightSideTransparent()
    {
        // Arrange
        var image = new Image<Rgba32>(100, 50);
        image.Mutate(ctx => ctx.Clear(Color.Red));

        // Act
        image.Mutate(ctx => ctx.ApplyGradientFade());

        // Assert
        Assert.Multiple(() =>
        {
            // Left side (before fade start) should be unchanged
            Assert.That(image[20, 25].A / 255f, Is.EqualTo(1.0f).Within(0.01f), "Left pixels should remain opaque");

            // Right side should have decreasing alpha
            Assert.That(image[90, 25].A / 255f, Is.LessThan(0.5f), "Right pixels should be more transparent");
            Assert.That(image[95, 25].A / 255f, Is.LessThan(image[85, 25].A / 255f),
                "Alpha should decrease toward the right edge");
        });
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(5)]
    public void GenerateStarRating_ShouldCreateCorrectNumberOfStars(int starCount)
    {
        // Act
        var result = ImageExtensions.GenerateStarRating(starCount);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Width, Is.EqualTo(30 * 5), "Image width should be 5 star widths");
            Assert.That(result.Height, Is.EqualTo(30), "Image height should be 1 star height");
        });

        // Verify the star count by checking pixels where stars should appear
        int centralY = 15; // Center of the star height

        // Calculate the offset based on star count
        int offset = (5 - starCount) * 30 / 2;

        // Check star centers (should be colored)
        for (int i = 0; i < starCount; i++)
        {
            int centralX = offset + i * 30 + 15; // Center of each star position

            // Ensure we're within image bounds
            if (centralX >= 0 && centralX < result.Width)
                // Star color should be present for active stars
                Assert.That(result[centralX, centralY].A, Is.Not.EqualTo(0),
                    $"Pixel at star {i + 1} center should be visible");
        }

        // For completeness, verify that areas outside the stars are transparent
        // Only check a few sample points to avoid boundary issues
        if (starCount < 5)
        {
            // Check a point clearly in the empty area (far right if stars are left-aligned)
            int emptyX = offset + starCount * 30 + 15;

            // Make sure we're safely within image bounds
            if (emptyX >= 0 && emptyX < result.Width - 10)
                Assert.That(result[emptyX, centralY].A, Is.EqualTo(0),
                    "Area outside stars should be transparent");
        }
    }

    [Test]
    public void GenerateStarRating_ShouldClampStarCount()
    {
        // Test with values outside the valid range
        var tooLow = ImageExtensions.GenerateStarRating(0);
        var tooHigh = ImageExtensions.GenerateStarRating(6);

        // Check if both are clamped properly by checking image properties
        var oneStar = ImageExtensions.GenerateStarRating(1);
        var fiveStars = ImageExtensions.GenerateStarRating(5);

        Assert.Multiple(() =>
        {
            // Compare pixel data at known positions
            Assert.That(tooLow[75, 15].A, Is.EqualTo(oneStar[75, 15].A), "0 stars should be clamped to 1 star");
            Assert.That(tooHigh[75, 15].A, Is.EqualTo(fiveStars[75, 15].A), "6 stars should be clamped to 5 stars");
        });
    }

    [Test]
    public void ApplyRoundedCorners_ShouldRoundTheCorners()
    {
        // Arrange
        var image = new Image<Rgba32>(100, 100);
        image.Mutate(ctx => ctx.Clear(Color.Blue));

        // Act
        image.Mutate(ctx => ctx.ApplyRoundedCorners(30));

        // Assert
        Assert.Multiple(() =>
        {
            // Check corners - they should be transparent
            Assert.That(image[5, 5].A, Is.EqualTo(0), "Top-left corner should be transparent");
            Assert.That(image[95, 5].A, Is.EqualTo(0), "Top-right corner should be transparent");
            Assert.That(image[5, 95].A, Is.EqualTo(0), "Bottom-left corner should be transparent");
            Assert.That(image[95, 95].A, Is.EqualTo(0), "Bottom-right corner should be transparent");

            // Center should still be visible
            Assert.That(image[50, 50].A, Is.Not.EqualTo(0), "Center should remain visible");

            // Points just inside the rounded radius should be visible
            Assert.That(image[25, 25].A, Is.Not.EqualTo(0), "Inside the corner radius should be visible");
        });
    }

    [Test]
    public void ApplyRoundedCorners_WithDifferentRadii()
    {
        // Arrange
        var smallRadius = new Image<Rgba32>(100, 100);
        var largeRadius = new Image<Rgba32>(100, 100);

        smallRadius.Mutate(ctx => ctx.Clear(Color.Blue));
        largeRadius.Mutate(ctx => ctx.Clear(Color.Blue));

        // Act
        smallRadius.Mutate(ctx => ctx.ApplyRoundedCorners(10));
        largeRadius.Mutate(ctx => ctx.ApplyRoundedCorners(30));

        // Assert
        Assert.Multiple(() =>
        {
            // The pixel at [15, 15] should be visible with small radius but transparent with large radius
            Assert.That(smallRadius[5, 5].A, Is.Not.EqualTo(0), "Pixel should be visible with small radius");
            Assert.That(largeRadius[5, 5].A, Is.EqualTo(0), "Pixel should be transparent with large radius");
        });
    }
}