using Mehrak.Domain.Shared.Utility;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Shared.Utility;

public static class GradientFadeExtensions
{
    /// <summary>
    /// Applies a horizontal gradient fade starting at a fractional position of the image width.
    /// Pixels from <paramref name="fadeStart"/> (0..1) to the right edge fade out.
    /// </summary>
    public static IImageProcessingContext ApplyGradientFade(this IImageProcessingContext context,
        float fadeStart = 0.75f, EasingType easing = EasingType.InQuint)
    {
        return context.ProcessPixelRowsAsVector4(row =>
        {
            var width = row.Length;
            var fadeStartX = (int)(width * fadeStart);
            for (var x = fadeStartX; x < width; x++)
            {
                var t = 1f - (float)(x - fadeStartX) / (width - fadeStartX);
                var alpha = Easing.Evaluate(easing, t);
                row[x].W *= alpha;
            }
        });
    }

    /// <summary>
    /// Applies a horizontal gradient fade between two pixel X-coordinates.
    /// Pixels before <paramref name="fadeStart"/> are unchanged.
    /// Pixels in [<paramref name="fadeStart"/>, <paramref name="fadeEnd"/>) fade out.
    /// Pixels at/after <paramref name="fadeEnd"/> are fully transparent.
    /// </summary>
    public static IImageProcessingContext ApplyGradientFade(this IImageProcessingContext context,
        int fadeStart, int fadeEnd, EasingType easing = EasingType.None)
    {
        return context.ProcessPixelRowsAsVector4(row =>
        {
            var width = row.Length;
            var fadeWidth = fadeEnd - fadeStart;
            if (fadeWidth <= 0) return;

            for (var x = Math.Max(0, fadeStart); x < Math.Min(fadeEnd, width); x++)
            {
                var t = 1f - (float)(x - fadeStart) / fadeWidth;
                row[x].W *= Easing.Evaluate(easing, t);
            }

            for (var x = Math.Max(0, fadeEnd); x < width; x++)
            {
                row[x].W = 0;
            }
        });
    }
}
