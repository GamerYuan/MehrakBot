#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record DropShadowTextStyle(
    Color? ShadowColor = null,
    float ShadowOffsetX = 3f,
    float ShadowOffsetY = 3f);

public static class DropShadowTextExtensions
{
    /// <summary>
    /// Draws text with an optional drop shadow. If BlurRadius > 0, the shadow is rendered
    /// to a temporary image and blurred before compositing.
    /// </summary>
    public static void DrawTextWithShadow(
        this DrawingCanvas canvas,
        string text,
        Font font,
        PointF origin,
        Color textColor,
        DropShadowTextStyle? style = null)
    {
        _ = canvas.SaveLayer();
        style ??= new DropShadowTextStyle();
        var shadowColor = style.ShadowColor ?? Color.Black;
        var shadowOrigin = new PointF(origin.X + style.ShadowOffsetX, origin.Y + style.ShadowOffsetY);

        canvas.DrawText(new RichTextOptions(font) { Origin = shadowOrigin }, text, Brushes.Solid(shadowColor), null);

        canvas.DrawText(new RichTextOptions(font) { Origin = origin }, text, Brushes.Solid(textColor), null);
        canvas.Restore();
    }

    /// <summary>
    /// Draws text with RichTextOptions and an optional drop shadow.
    /// </summary>
    public static void DrawTextWithShadow(
        this DrawingCanvas canvas,
        string text,
        RichTextOptions options,
        Color textColor,
        DropShadowTextStyle? style = null)
    {
        _ = canvas.SaveLayer();
        style ??= new DropShadowTextStyle();
        var shadowColor = style.ShadowColor ?? Color.Black;

        var shadowOptions = new RichTextOptions(options)
        {
            Origin = new PointF(options.Origin.X + style.ShadowOffsetX, options.Origin.Y + style.ShadowOffsetY)
        };
        canvas.DrawText(shadowOptions, text, Brushes.Solid(shadowColor), null);

        canvas.DrawText(options, text, Brushes.Solid(textColor), null);
        canvas.Restore();
    }
}
