using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Extensions;

public static class ImageIconExtensions
{
    public static IImageProcessingContext DrawCenteredIcon(this IImageProcessingContext ctx, Image icon, PointF center, float radius,
        float padding = 0, Color? background = null, Color? outline = null)
    {
        var ellipse = new EllipsePolygon(center, radius);

        if (background.HasValue)
        {
            ctx.Fill(background.Value, ellipse);
        }
        if (outline.HasValue)
        {
            ctx.Draw(outline.Value, 2, ellipse);
        }

        var iconSize = (radius * 2 - padding) / Math.Sqrt(2);
        using var resizedIcon = icon.Clone(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size((int)iconSize, (int)iconSize)
        }));

        var iconPosition = new Point((int)(center.X - resizedIcon.Width / 2), (int)(center.Y - resizedIcon.Height / 2));

        ctx.DrawImage(resizedIcon, iconPosition, 1f);
        return ctx;
    }
}
