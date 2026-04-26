#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Renderers.Extensions;

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
    public static IImageProcessingContext DrawTextWithShadow(
        this IImageProcessingContext ctx,
        string text,
        Font font,
        PointF origin,
        Color textColor,
        DropShadowTextStyle? style = null)
    {
        style ??= new DropShadowTextStyle();
        var shadowColor = style.ShadowColor ?? Color.Black;
        var shadowOrigin = new PointF(origin.X + style.ShadowOffsetX, origin.Y + style.ShadowOffsetY);

        if (style.BlurRadius > 0)
        {
            DrawBlurredShadow(ctx, text, font, shadowOrigin, shadowColor, style.BlurRadius);
        }
        else
        {
            ctx.DrawText(text, font, shadowColor, shadowOrigin);
        }

        ctx.DrawText(text, font, textColor, origin);
        return ctx;
    }

    /// <summary>
    /// Draws text with RichTextOptions and an optional drop shadow.
    /// </summary>
    public static IImageProcessingContext DrawTextWithShadow(
        this IImageProcessingContext ctx,
        string text,
        RichTextOptions options,
        Color textColor,
        DropShadowTextStyle? style = null)
    {
        style ??= new DropShadowTextStyle();
        var shadowColor = style.ShadowColor ?? Color.Black;

        if (style.BlurRadius > 0)
        {
            DrawBlurredShadow(ctx, text, options, shadowColor, style);
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
            ctx.DrawText(shadowOptions, text, shadowColor);
        }

        ctx.DrawText(options, text, textColor);
        return ctx;
    }

    private static void DrawBlurredShadow(
        IImageProcessingContext ctx,
        string text,
        Font font,
        PointF shadowOrigin,
        Color shadowColor,
        float blurRadius)
    {
        var blurR = (int)Math.Ceiling(blurRadius);
        var pad = blurR + 4;

        var bounds = TextMeasurer.MeasureBounds(text, new RichTextOptions(font) { Origin = PointF.Empty });
        var w = (int)Math.Ceiling(bounds.Width) + pad * 2;
        var h = (int)Math.Ceiling(bounds.Height) + pad * 2;

        using var shadowImg = new Image<Rgba32>(Math.Max(1, w), Math.Max(1, h));
        shadowImg.Mutate(x =>
        {
            x.DrawText(text, font, shadowColor, new PointF(pad - bounds.Left, pad - bounds.Top));
            x.BoxBlur(blurR);
        });


        ctx.DrawImage(shadowImg,
            new Point((int)Math.Floor(shadowOrigin.X + bounds.Left - pad),
            (int)Math.Floor(shadowOrigin.Y + bounds.Top - pad)), 1f);
    }

    private static void DrawBlurredShadow(
        IImageProcessingContext ctx,
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
        var w = (int)Math.Ceiling(bounds.Width) + pad * 2;
        var h = (int)Math.Ceiling(bounds.Height) + pad * 2;
        var imgX = (int)Math.Floor(bounds.Left - pad);
        var imgY = (int)Math.Floor(bounds.Top - pad);

        using var shadowImg = new Image<Rgba32>(Math.Max(1, w), Math.Max(1, h));
        shadowImg.Mutate(x =>
        {
            var drawOptions = new RichTextOptions(options.Font)
            {
                Origin = new PointF(shadowOptions.Origin.X - imgX, shadowOptions.Origin.Y - imgY),
                WrappingLength = options.WrappingLength,
                HorizontalAlignment = options.HorizontalAlignment,
                VerticalAlignment = options.VerticalAlignment
            };
            x.DrawText(drawOptions, text, shadowColor);
            x.BoxBlur(blurR);
        });

        ctx.DrawImage(shadowImg, new Point(imgX, imgY), 1f);
    }
}
