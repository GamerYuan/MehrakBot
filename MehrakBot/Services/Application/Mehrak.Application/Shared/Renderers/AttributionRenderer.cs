#region

using System.Numerics;
using Mehrak.Application.Shared.Renderers.Extensions;
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
    {
        var finalText = extraText != null ? $"{extraText}\n{Text}" : Text;

        style ??= Default;
        var needsRotation = Math.Abs(style.RotationDegrees) > 0.001f;
        var needsOpacity = style.Opacity is > 0f and < 1f;

        if (needsRotation)
        {
            DrawingOptions drawingOptions = new()
            {
                Transform = new(
                    Matrix3x2.CreateRotation(
                        MathF.PI * style.RotationDegrees / 180f,
                        textOptions.Origin
                    )
                ),
            };
            _ = canvas.Save(drawingOptions);
        }

        if (needsOpacity)
        {
            _ = canvas.SaveLayer(new GraphicsOptions { BlendPercentage = style.Opacity });
        }

        var textColor = style.TextColor ?? Color.White;

        if (style.ShadowStyle != null)
        {
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

    public static void DrawAttribution(this DrawingCanvas canvas, RichTextOptions textOptions)
    {
        DrawAttribution(canvas, textOptions, style: null, extraText: null);
    }

    public static void DrawAttribution(
        this DrawingCanvas canvas,
        RichTextOptions textOptions,
        AttributionStyle? style = null
    )
    {
        DrawAttribution(canvas, textOptions, style, extraText: null);
    }
}
