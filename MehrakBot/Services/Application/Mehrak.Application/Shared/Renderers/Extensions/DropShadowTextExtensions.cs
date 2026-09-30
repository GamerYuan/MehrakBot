#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record DropShadowTextStyle(
    Color? ShadowColor = null,
    float ShadowOffsetX = 3f,
    float ShadowOffsetY = 3f);

public static class DropShadowTextExtensions
{
    /// <summary>
    /// Draws text with an optional drop shadow.
    /// </summary>
    public static void DrawTextWithShadow(
        this DrawingCanvas canvas,
        string text,
        Font font,
        PointF origin,
        Color textColor,
        DropShadowTextStyle? style = null)
        => DrawTextWithShadowCore(canvas, text, new RichTextOptions(font) { Origin = origin }, textColor, style, false);

    internal static void DrawBoundedTextWithShadow(
        this DrawingCanvas canvas,
        string text,
        Font font,
        PointF origin,
        Color textColor,
        DropShadowTextStyle? style = null)
        => DrawTextWithShadowCore(canvas, text, new RichTextOptions(font) { Origin = origin }, textColor, style, true);

    /// <summary>
    /// Draws text with RichTextOptions and an optional drop shadow.
    /// </summary>
    public static void DrawTextWithShadow(
        this DrawingCanvas canvas,
        string text,
        RichTextOptions options,
        Color textColor,
        DropShadowTextStyle? style = null)
        => DrawTextWithShadowCore(canvas, text, options, textColor, style, false);

    internal static void DrawBoundedTextWithShadow(
        this DrawingCanvas canvas,
        string text,
        RichTextOptions options,
        Color textColor,
        DropShadowTextStyle? style = null)
        => DrawTextWithShadowCore(canvas, text, options, textColor, style, true);

    private static void DrawTextWithShadowCore(
        DrawingCanvas canvas,
        string text,
        RichTextOptions options,
        Color textColor,
        DropShadowTextStyle? style,
        bool useBoundedLayer)
    {
        style ??= new DropShadowTextStyle();
        var shadowColor = style.ShadowColor ?? Color.Black;
        var shadowOptions = new RichTextOptions(options)
        {
            Origin = new PointF(options.Origin.X + style.ShadowOffsetX, options.Origin.Y + style.ShadowOffsetY)
        };

        if (useBoundedLayer)
        {
            _ = LayerBoundsUtility.SaveLayer(canvas,
                LayerBoundsUtility.GetTextBounds(text, shadowOptions),
                LayerBoundsUtility.GetTextBounds(text, options));
        }
        else
        {
            _ = canvas.SaveLayer();
        }

        canvas.DrawText(shadowOptions, text, Brushes.Solid(shadowColor), null);
        canvas.DrawText(options, text, Brushes.Solid(textColor), null);
        canvas.Restore();
    }
}
