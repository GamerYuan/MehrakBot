#region

using Mehrak.Application.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Renderers.Extensions;

public record RoundedRectangleOverlayStyle(
    Color FillColor,
    Color? BorderColor = null,
    float BorderWidth = 0f,
    int CornerRadius = 20);

public static class RoundedRectangleExtensions
{
    public static IImageProcessingContext DrawRoundedRectangleOverlay(
        this IImageProcessingContext ctx,
        int width,
        int height,
        PointF location,
        RoundedRectangleOverlayStyle style)
    {
        var path = ImageUtility.CreateRoundedRectanglePath(width, height, style.CornerRadius)
            .Translate(location);

        ctx.Fill(style.FillColor, path);

        if (style.BorderColor.HasValue && style.BorderWidth > 0)
        {
            ctx.Draw(style.BorderColor.Value, style.BorderWidth, path);
        }

        return ctx;
    }
}
