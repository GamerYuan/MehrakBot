using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Renderers.Extensions;

public static class ImageIconExtensions
{
    public static void DrawCenteredIcon(this DrawingCanvas canvas, Image icon, PointF center, float radius,
        float padding = 0, Color? background = null, Color? outline = null)
    {
        canvas.DrawCenteredIcon(icon, center, radius, padding,
            background ?? Color.Transparent, outline ?? Color.Transparent, 2f);
    }

    public static void DrawCenteredIcon(this DrawingCanvas canvas, Image icon, PointF center, float radius,
        float padding, Color background, Color outline, float outlineWidth)
    {
        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");

        _ = canvas.SaveLayer();
        var iconSize = (radius - padding) * 2;

        if (iconSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding must be smaller than the icon diameter.");

        var ellipse = new EllipsePolygon(center, radius);

        canvas.Fill(Brushes.Solid(background), ellipse);
        canvas.Draw(Pens.Solid(outline, outlineWidth), ellipse);

        var iconPosition = new Point((int)(center.X - iconSize / 2), (int)(center.Y - iconSize / 2));

        canvas.DrawImage(icon, icon.Bounds,
            new RectangleF(iconPosition.X, iconPosition.Y, iconSize, iconSize), KnownResamplers.Bicubic);
        canvas.Restore();
    }
}
