using Mehrak.Application.Shared.Renderers.Extensions;
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
        DrawCenteredIconCore(canvas, icon, center, radius, padding,
            background ?? Color.Transparent, outline ?? Color.Transparent, 2f, false);
    }

    public static void DrawCenteredIcon(this DrawingCanvas canvas, Image icon, PointF center, float radius,
        float padding, Color background, Color outline, float outlineWidth)
        => DrawCenteredIconCore(canvas, icon, center, radius, padding, background, outline, outlineWidth, false);

    internal static void DrawBoundedCenteredIcon(this DrawingCanvas canvas, Image icon, PointF center, float radius,
        float padding = 0, Color? background = null, Color? outline = null)
    {
        DrawCenteredIconCore(canvas, icon, center, radius, padding,
            background ?? Color.Transparent, outline ?? Color.Transparent, 2f, true);
    }

    internal static void DrawBoundedCenteredIcon(this DrawingCanvas canvas, Image icon, PointF center, float radius,
        float padding, Color background, Color outline, float outlineWidth)
        => DrawCenteredIconCore(canvas, icon, center, radius, padding, background, outline, outlineWidth, true);

    private static void DrawCenteredIconCore(DrawingCanvas canvas, Image icon, PointF center, float radius,
        float padding, Color background, Color outline, float outlineWidth, bool useBoundedLayer)
    {
        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");

        var iconSize = (radius - padding) * 2;
        if (iconSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding must be smaller than the icon diameter.");

        var ellipse = new EllipsePolygon(center, radius);
        var iconPosition = new Point((int)(center.X - iconSize / 2), (int)(center.Y - iconSize / 2));
        var iconDestination = new RectangleF(iconPosition.X, iconPosition.Y, iconSize, iconSize);

        if (useBoundedLayer)
        {
            var outlineExtent = Math.Max(0, outlineWidth) / 2f + LayerBoundsUtility.AntialiasingFringe;
            var ellipseBounds = LayerBoundsUtility.Inflate(
                new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2), outlineExtent);
            _ = LayerBoundsUtility.SaveLayer(canvas, ellipseBounds,
                LayerBoundsUtility.Inflate(iconDestination, LayerBoundsUtility.AntialiasingFringe));
        }
        else
        {
            _ = canvas.SaveLayer();
        }

        canvas.Fill(Brushes.Solid(background), ellipse);
        canvas.Draw(Pens.Solid(outline, outlineWidth), ellipse);
        canvas.DrawImage(icon, icon.Bounds, iconDestination, KnownResamplers.Bicubic);
        canvas.Restore();
    }
}
