using System.Numerics;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Zzz.Character;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Tests.Zzz.Character;

public sealed class ZzzCharacterIconRenderingTests
{
    [TestCase(80, 80, 1110)]
    [TestCase(70, 90, 1240)]
    public void RotatedIcon_MatchesUnclippedReference(int iconWidth, int iconHeight, int yOffset)
    {
        const float gradient = 100f / 1400f;
        var location = new Point(710 - (int)MathF.Round(yOffset * gradient), yOffset);
        using var icon = new Image<Rgba32>(iconWidth, iconHeight, Color.White.ToPixel<Rgba32>());
        using var expected = new Image<Rgba32>(850, 1400);
        using var actual = new Image<Rgba32>(850, 1400);

        // Draw on the full canvas so neither the rotated path nor its centered stroke is clipped.
        expected.Mutate(ctx => ctx.Paint(canvas =>
        {
            _ = canvas.Save(new DrawingOptions
            {
                Transform = new(Matrix3x2.CreateRotation(MathF.Atan(gradient)) *
                    Matrix3x2.CreateTranslation(location.X, location.Y))
            });
            canvas.DrawRoundedRectangleOverlay(90, 120, PointF.Empty,
                new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgb24(40, 40, 40)), Color.Red, 4f, 10));
            canvas.DrawImage(icon, icon.Bounds,
                new RectangleF(45 - icon.Width / 2, 60 - icon.Height / 2, icon.Width, icon.Height),
                KnownResamplers.Bicubic);
            canvas.Restore();
        }));

        actual.Mutate(ctx => ctx.Paint(canvas =>
        {
            var saveCount = canvas.SaveCount;
            ZzzCharacterCardService.DrawRotatedIconImage(canvas, location, icon, Color.Red);
            Assert.That(canvas.SaveCount, Is.EqualTo(saveCount));
        }));

        var differingPixels = 0;
        for (var y = location.Y - 20; y < location.Y + 140; y++)
        {
            for (var x = location.X - 20; x < location.X + 110; x++)
            {
                var actualPixel = actual[x, y];
                var expectedPixel = expected[x, y];
                // Equivalent transforms can differ slightly in floating-point rasterization.
                if (Math.Abs(actualPixel.R - expectedPixel.R) > 2 ||
                    Math.Abs(actualPixel.G - expectedPixel.G) > 2 ||
                    Math.Abs(actualPixel.B - expectedPixel.B) > 2 ||
                    Math.Abs(actualPixel.A - expectedPixel.A) > 2)
                    differingPixels++;
            }
        }

        Assert.That(differingPixels, Is.Zero, "The icon and its complete border must match the unclipped rendering.");
    }
}
