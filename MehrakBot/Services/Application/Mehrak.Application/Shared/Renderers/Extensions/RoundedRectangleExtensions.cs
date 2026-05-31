#region

using Mehrak.Application.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record RoundedRectangleOverlayStyle(
    Color FillColor,
    Color? BorderColor = null,
    float BorderWidth = 0f,
    int CornerRadius = 20);

public static class RoundedRectangleExtensions
{
    public static void DrawRoundedRectangleOverlay(
        this DrawingCanvas canvas,
        int width,
        int height,
        PointF location,
        RoundedRectangleOverlayStyle style)
    {
        var roundedRect = new RoundedRectanglePolygon(new RectangleF(location, new Size(width, height)), style.CornerRadius);

        _ = canvas.SaveLayer();
        canvas.Fill(Brushes.Solid(style.FillColor), roundedRect);

        if (style.BorderColor.HasValue && style.BorderWidth > 0)
        {
            canvas.Draw(Pens.Solid(style.BorderColor.Value, style.BorderWidth), roundedRect);
        }
        canvas.Restore();
    }
}
