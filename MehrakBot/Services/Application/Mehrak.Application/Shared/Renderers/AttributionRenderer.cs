#region

using System.Numerics;
using Mehrak.Application.Shared.Renderers.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Shared.Renderers;

public record AttributionStyle(
    Color? TextColor = null,
    Color? OutlineColor = null,
    float OutlineWidth = 1f,
    float Opacity = 1f,
    float RotationDegrees = 0f,
    DropShadowTextStyle? ShadowStyle = null
);

public static class AttributionRenderer
{
    public static readonly AttributionStyle Default = new(
        TextColor: Color.White,
        ShadowStyle: new DropShadowTextStyle(
            ShadowOffsetX: 2,
            ShadowOffsetY: 2,
            ShadowColor: Color.FromPixel(new Rgba32(0, 0, 0, 0.75f)))
    );

    private static readonly string[] Lines = ["MehrakBot", "mehrakbot.com"];
    private static readonly string Text = string.Join("\n", Lines);

    public static void DrawAttribution(this DrawingCanvas canvas, RichTextOptions textOptions,
        AttributionStyle? style = null, string? extraText = null)
        => DrawAttributionCore(canvas, textOptions, style, extraText, false);

    internal static void DrawBoundedAttribution(this DrawingCanvas canvas, RichTextOptions textOptions,
        AttributionStyle? style = null, string? extraText = null)
        => DrawAttributionCore(canvas, textOptions, style, extraText, true);

    private static void DrawAttributionCore(DrawingCanvas canvas, RichTextOptions textOptions,
        AttributionStyle? style, string? extraText, bool useBoundedLayers)
    {
        var finalText = extraText != null ? $"{extraText}\n{Text}" : Text;
        style ??= Default;
        var needsRotation = Math.Abs(style.RotationDegrees) > 0.001f;
        var needsOpacity = style.Opacity is > 0f and < 1f;

        if (needsRotation)
        {
            DrawingOptions drawingOptions = new()
            {
                Transform = new(Matrix3x2.CreateRotation(
                    MathF.PI * style.RotationDegrees / 180f,
                    textOptions.Origin))
            };
            _ = canvas.Save(drawingOptions);
        }

        if (needsOpacity)
        {
            var graphicsOptions = new GraphicsOptions { BlendPercentage = style.Opacity };
            if (useBoundedLayers)
            {
                var bounds = GetAttributionBounds(finalText, textOptions, style);
                _ = LayerBoundsUtility.SaveLayer(canvas, graphicsOptions, bounds);
            }
            else
            {
                _ = canvas.SaveLayer(graphicsOptions);
            }
        }

        var textColor = style.TextColor ?? Color.White;
        if (style.ShadowStyle != null)
        {
            if (useBoundedLayers)
                canvas.DrawBoundedTextWithShadow(finalText, textOptions, textColor, style.ShadowStyle);
            else
                canvas.DrawTextWithShadow(finalText, textOptions, textColor, style.ShadowStyle);
        }
        else
        {
            var brush = Brushes.Solid(textColor);
            var pen = style.OutlineColor.HasValue
                ? Pens.Solid(style.OutlineColor.Value, style.OutlineWidth)
                : null;
            canvas.DrawText(textOptions, finalText, brush, pen);
        }

        if (needsOpacity)
            canvas.Restore();
        if (needsRotation)
            canvas.Restore();
    }

    private static RectangleF GetAttributionBounds(string text, RichTextOptions options, AttributionStyle style)
    {
        if (style.ShadowStyle == null)
        {
            var outlineExtent = style.OutlineColor.HasValue ? Math.Max(0, style.OutlineWidth) / 2f : 0;
            return LayerBoundsUtility.GetTextBounds(text, options, additionalExtent: outlineExtent);
        }

        var shadowOptions = new RichTextOptions(options)
        {
            Origin = new PointF(options.Origin.X + style.ShadowStyle.ShadowOffsetX,
                options.Origin.Y + style.ShadowStyle.ShadowOffsetY)
        };
        var textBounds = LayerBoundsUtility.GetTextBounds(text, options);
        var shadowBounds = LayerBoundsUtility.GetTextBounds(text, shadowOptions);
        return Union(textBounds, shadowBounds);
    }

    private static RectangleF Union(RectangleF first, RectangleF second)
    {
        if (!IsFinite(first) || !IsFinite(second))
            return new RectangleF(float.NaN, float.NaN, float.NaN, float.NaN);

        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static bool IsFinite(RectangleF bounds)
        => float.IsFinite(bounds.X) && float.IsFinite(bounds.Y)
           && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height);

    public static void DrawAttribution(this DrawingCanvas canvas, RichTextOptions textOptions)
    {
        DrawAttribution(canvas, textOptions, style: null, extraText: null);
    }

    public static void DrawAttribution(
        this DrawingCanvas canvas,
        RichTextOptions textOptions,
        AttributionStyle? style = null)
    {
        DrawAttribution(canvas, textOptions, style, extraText: null);
    }
}
