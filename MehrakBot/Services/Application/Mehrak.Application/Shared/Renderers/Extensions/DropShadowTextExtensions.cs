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
    float ShadowOffsetY = 3f,
    float BlurRadius = 0f);

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

        if (style.BlurRadius > 0)
        {
            DrawBlurredShadow(canvas, text, font, shadowOrigin, shadowColor, style.BlurRadius);
        }
        else
        {
            canvas.DrawText(new RichTextOptions(font) { Origin = shadowOrigin }, text, Brushes.Solid(shadowColor), null);
        }

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
        style ??= new DropShadowTextStyle();
        var shadowColor = style.ShadowColor ?? Color.Black;

        _ = canvas.SaveLayer();
        if (style.BlurRadius > 0)
        {
            DrawBlurredShadow(canvas, text, options, shadowColor, style);
        }
        else
        {
            var shadowOptions = new RichTextOptions(options.Font)
            {
                Origin = new PointF(options.Origin.X + style.ShadowOffsetX, options.Origin.Y + style.ShadowOffsetY),
                WrappingLength = options.WrappingLength,
                HorizontalAlignment = options.HorizontalAlignment,
                VerticalAlignment = options.VerticalAlignment
            };
            canvas.DrawText(shadowOptions, text, Brushes.Solid(shadowColor), null);
        }

        canvas.DrawText(options, text, Brushes.Solid(textColor), null);
        canvas.Restore();
    }

    private static void DrawBlurredShadow(
        DrawingCanvas canvas,
        string text,
        Font font,
        PointF shadowOrigin,
        Color shadowColor,
        float blurRadius)
    {
        var blurR = (int)Math.Ceiling(blurRadius);

        var bounds = TextMeasurer.MeasureBounds(text, new RichTextOptions(font) { Origin = PointF.Empty });
        var pad = blurR + 4;

        _ = canvas.SaveLayer();
        canvas.DrawText(new RichTextOptions(font) { Origin = shadowOrigin }, text, Brushes.Solid(shadowColor), null);
        canvas.Apply(
            new Rectangle(
                new Point((int)(shadowOrigin.X + bounds.X) - pad, (int)(shadowOrigin.Y + bounds.Y) - pad),
                new Size((int)bounds.Width + 2 * pad, (int)bounds.Height + 2 * pad)),
            ctx => ctx.BoxBlur(blurR));
        canvas.Restore();
    }

    private static void DrawBlurredShadow(
        DrawingCanvas canvas,
        string text,
        RichTextOptions options,
        Color shadowColor,
        DropShadowTextStyle style)
    {
        var blurR = (int)Math.Ceiling(style.BlurRadius);
        var pad = blurR + 4;

        var shadowOptions = new RichTextOptions(options.Font)
        {
            Origin = new PointF(options.Origin.X + style.ShadowOffsetX, options.Origin.Y + style.ShadowOffsetY),
            WrappingLength = options.WrappingLength,
            HorizontalAlignment = options.HorizontalAlignment,
            VerticalAlignment = options.VerticalAlignment
        };

        var bounds = TextMeasurer.MeasureBounds(text, shadowOptions);

        _ = canvas.SaveLayer();
        canvas.DrawText(shadowOptions, text, Brushes.Solid(shadowColor), null);
        canvas.Apply(
            new Rectangle(new Point((int)shadowOptions.Origin.X - pad, (int)shadowOptions.Origin.Y - pad),
                new Size((int)bounds.Width + 2 * pad, (int)bounds.Height + 2 * pad)),
            ctx => ctx.BoxBlur(blurR));
        canvas.Restore();
    }
}
