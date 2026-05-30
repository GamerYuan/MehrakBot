using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Renderers.Extensions;

public static class ImageIconExtensions
{
    public static IImageProcessingContext DrawCenteredIcon(this IImageProcessingContext ctx, Image icon, PointF center, float radius,
        float padding = 0, Color? background = null, Color? outline = null)
    {
        return ctx.DrawCenteredIcon(icon, center, radius, padding,
            background ?? Color.Transparent, outline ?? Color.Transparent, 2f);
    }

    public static IImageProcessingContext DrawCenteredIcon(this IImageProcessingContext ctx, Image icon, PointF center, float radius,
        float padding, Color background, Color outline, float outlineWidth)
    {
        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");

        var iconSize = (radius - padding) * 2;

        if (iconSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding must be smaller than the icon diameter.");

        var ellipse = new EllipsePolygon(center, radius);

        ctx.Fill(background, ellipse);
        ctx.Draw(outline, outlineWidth, ellipse);

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
