#region

using System.Numerics;
using Mehrak.Application.Shared.Renderers.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers;

public record AttributionStyle(
    PointF Position,
    Color? TextColor = null,
    Color? OutlineColor = null,
    float OutlineWidth = 1f,
    float Opacity = 1f,
    float RotationDegrees = 0f,
    DropShadowTextStyle? ShadowStyle = null
);

public static class AttributionRenderer
{
    private static readonly string[] Lines = ["MehrakBot", "mehrak.yuan-dev.com"];
    private static readonly string Text = string.Join("\n", Lines);

    public static void DrawAttribution(
        this DrawingCanvas canvas,
        AttributionStyle style,
        RichTextOptions textOptions
    )
    {
        var needsRotation = Math.Abs(style.RotationDegrees) > 0.001f;
        var needsOpacity = style.Opacity is > 0f and < 1f;

        if (needsRotation)
        {
            DrawingOptions drawingOptions = new()
            {
                Transform = new(
                    Matrix3x2.CreateRotation(
                        MathF.PI * style.RotationDegrees / 180f,
                        style.Position
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
            canvas.DrawTextWithShadow(Text, textOptions, textColor, style.ShadowStyle);
        }
        else
        {
            var brush = Brushes.Solid(textColor);
            var pen = style.OutlineColor.HasValue
                ? Pens.Solid(style.OutlineColor.Value, style.OutlineWidth)
                : null;
            canvas.DrawText(textOptions, Text, brush, pen);
        }

        if (needsOpacity)
            canvas.Restore();

        if (needsRotation)
            canvas.Restore();
    }
}
