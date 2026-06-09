#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record EllipseTextStyle(
    Font Font,
    Color TextColor,
    Color Background,
    Color? Outline = null,
    float OutlineWidth = 0f,
    bool ShrinkToFit = true);

public static class EllipseTextExtensions
{
    /// <summary>
    /// Draws a filled ellipse with optionally outlined border, and renders text
    /// visually centered inside it. If ShrinkToFit is true, the font will be
    /// reduced until the text fits within the ellipse diameter.
    /// </summary>
    public static void DrawCenteredTextInEllipse(
        this DrawingCanvas canvas,
        string text,
        PointF center,
        float radius,
        EllipseTextStyle style)
    {
        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");

        _ = canvas.SaveLayer();
        var ellipse = new EllipsePolygon(center, radius);
        canvas.Fill(Brushes.Solid(style.Background), ellipse);

        if (style.Outline != null && style.OutlineWidth > 0)
        {
            canvas.Draw(Pens.Solid(style.Outline.Value, style.OutlineWidth), ellipse);
        }

        var actualFont = style.ShrinkToFit
            ? GetFittingFont(text, style.Font, radius * 2 - 4)
            : style.Font;

        var bounds = TextMeasurer.MeasureBounds(text, new RichTextOptions(actualFont)
        {
            Origin = PointF.Empty
        });

        var drawY = (center.Y - radius * 0.05f) - (bounds.Height / 2f);

        canvas.DrawText(new RichTextOptions(actualFont)
        {
            Origin = new PointF(center.X, drawY),
            HorizontalAlignment = HorizontalAlignment.Center
        }, text, Brushes.Solid(style.TextColor), null);
        canvas.Restore();
    }

    private static Font GetFittingFont(string text, Font font, float maxWidth)
    {
        var currentFont = font;
        var size = TextMeasurer.MeasureBounds(text, new RichTextOptions(currentFont));

        while (size.Width > maxWidth && currentFont.Size > 8)
        {
            currentFont = currentFont.Family.CreateFont(currentFont.Size - 1);
            size = TextMeasurer.MeasureBounds(text, new RichTextOptions(currentFont));
        }

        return currentFont;
    }
}
