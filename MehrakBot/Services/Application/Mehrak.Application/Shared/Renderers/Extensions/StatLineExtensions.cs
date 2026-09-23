#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record StatLineData(
    string StatName,
    string FinalValue,
    string? BaseValue = null,
    string? BonusValue = null);

public record StatLineStyle(
    Image? StatIcon,
    Font Font,
    Color TextColor,
    Font? BreakdownFont = null,
    Color? BaseColor = null,
    Color? BonusColor = null);

public static class StatLineExtensions
{
    private const int IconSize = 48;
    private const int IconTextSpacing = 16;
    private const float BreakdownGap = 5f;
    private const float MaxNameWidthRatio = 0.65f;

    /// <summary>
    /// Draws a stat line with an icon, stat name, right-aligned final value,
    /// and optional base/bonus breakdown rendered below.
    /// </summary>
    public static void DrawStatLine(
        this DrawingCanvas canvas,
        StatLineData data,
        StatLineStyle style,
        PointF position,
        float width)
        => DrawStatLineCore(canvas, data, style, position, width, false);

    internal static void DrawBoundedStatLine(
        this DrawingCanvas canvas,
        StatLineData data,
        StatLineStyle style,
        PointF position,
        float width)
        => DrawStatLineCore(canvas, data, style, position, width, true);

    private static void DrawStatLineCore(
        DrawingCanvas canvas,
        StatLineData data,
        StatLineStyle style,
        PointF position,
        float width,
        bool useBoundedLayer)
    {
        var hasBreakdown = style.BreakdownFont != null
                           && !string.IsNullOrEmpty(data.BaseValue) && !string.IsNullOrEmpty(data.BonusValue);

        var rightAlignX = position.X + width;
        var iconTop = position.Y;
        var mainCenterY = iconTop + IconSize * 0.5f;
        var breakdownCenterY = hasBreakdown ? iconTop + IconSize * 0.9f : mainCenterY;
        var valueCenterY = hasBreakdown ? iconTop + IconSize * 0.2f : mainCenterY;
        var nameX = position.X + IconSize + IconTextSpacing;

        var maxNameWidth = width * MaxNameWidthRatio;
        if (maxNameWidth < 10) maxNameWidth = 10;

        var nameOptions = new RichTextOptions(style.Font)
        {
            Origin = new PointF(nameX, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        if (TextMeasurer.MeasureBounds(data.StatName, nameOptions).Width > maxNameWidth)
        {
            var fittingFont = GetFittingFontWithWrapping(data.StatName, style.Font, maxNameWidth, IconSize * 1.25f);
            nameOptions = new RichTextOptions(fittingFont)
            {
                Origin = new PointF(nameX, 0),
                VerticalAlignment = VerticalAlignment.Top,
                WrappingLength = maxNameWidth
            };
        }

        nameOptions.Origin = new PointF(nameX, GetVisualCenterY(data.StatName, nameOptions, mainCenterY));
        var finalValueOptions = new RichTextOptions(style.Font)
        {
            Origin = new PointF(rightAlignX, GetVisualCenterY(data.FinalValue, style.Font, valueCenterY)),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        RichTextOptions? bonusOptions = null;
        RichTextOptions? baseOptions = null;
        var baseColor = style.BaseColor ?? Color.LightGray;
        var bonusColor = style.BonusColor ?? Color.LightGreen;
        if (hasBreakdown)
        {
            var breakdownFont = style.BreakdownFont!;
            var baseTextX = rightAlignX;
            if (!string.IsNullOrEmpty(data.BonusValue))
            {
                var bonusSize = TextMeasurer.MeasureBounds(data.BonusValue, new RichTextOptions(breakdownFont));
                bonusOptions = new RichTextOptions(breakdownFont)
                {
                    Origin = new PointF(rightAlignX, GetVisualCenterY(data.BonusValue, breakdownFont, breakdownCenterY)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                baseTextX = rightAlignX - bonusSize.Width - BreakdownGap;
            }

            if (!string.IsNullOrEmpty(data.BaseValue))
            {
                baseOptions = new RichTextOptions(breakdownFont)
                {
                    Origin = new PointF(baseTextX, GetVisualCenterY(data.BaseValue, breakdownFont, breakdownCenterY)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
            }
        }

        if (useBoundedLayer)
        {
            var layerBounds = new List<RectangleF>
            {
                LayerBoundsUtility.GetTextBounds(data.StatName, nameOptions),
                LayerBoundsUtility.GetTextBounds(data.FinalValue, finalValueOptions)
            };
            if (style.StatIcon != null)
            {
                layerBounds.Add(LayerBoundsUtility.Inflate(
                    new RectangleF((int)position.X, (int)iconTop, IconSize, IconSize),
                    LayerBoundsUtility.AntialiasingFringe));
            }
            if (bonusOptions != null)
                layerBounds.Add(LayerBoundsUtility.GetTextBounds(data.BonusValue!, bonusOptions));
            if (baseOptions != null)
                layerBounds.Add(LayerBoundsUtility.GetTextBounds(data.BaseValue!, baseOptions));
            _ = LayerBoundsUtility.SaveLayer(canvas, [.. layerBounds]);
        }
        else
        {
            _ = canvas.SaveLayer();
        }

        if (style.StatIcon != null)
        {
            canvas.DrawImage(style.StatIcon, style.StatIcon.Bounds,
                new RectangleF(new Point((int)position.X, (int)iconTop), new Size(IconSize, IconSize)), KnownResamplers.Bicubic);
        }
        canvas.DrawText(nameOptions, data.StatName, Brushes.Solid(style.TextColor), null);
        canvas.DrawText(finalValueOptions, data.FinalValue, Brushes.Solid(style.TextColor), null);
        if (bonusOptions != null)
            canvas.DrawText(bonusOptions, data.BonusValue!, Brushes.Solid(bonusColor), null);
        if (baseOptions != null)
            canvas.DrawText(baseOptions, data.BaseValue!, Brushes.Solid(baseColor), null);
        canvas.Restore();
    }

    private static float GetVisualCenterY(string text, Font font, float targetCenterY)
    {
        var size = TextMeasurer.MeasureBounds(text, new RichTextOptions(font)
        {
            Origin = PointF.Empty,
        });
        return targetCenterY - size.Height / 2f;
    }

    private static float GetVisualCenterY(string text, RichTextOptions options, float targetCenterY)
    {
        var measureOptions = new RichTextOptions(options.Font)
        {
            Origin = PointF.Empty,
            WrappingLength = options.WrappingLength,
            VerticalAlignment = options.VerticalAlignment,
            HorizontalAlignment = options.HorizontalAlignment,
            LineSpacing = options.LineSpacing,
            TextAlignment = options.TextAlignment
        };
        var size = TextMeasurer.MeasureBounds(text, measureOptions);
        return targetCenterY - size.Height / 2f;
    }

    private static Font GetFittingFontWithWrapping(string text, Font font, float maxWidth, float maxHeight)
    {
        const int maxLines = 2;
        var currentFont = font;

        while (currentFont.Size > 10)
        {
            var options = new RichTextOptions(currentFont) { WrappingLength = maxWidth };
            var size = TextMeasurer.MeasureBounds(text, options);
            var lines = TextMeasurer.CountLines(text, options);
            if (size.Height <= maxHeight && lines <= maxLines)
                return currentFont;
            currentFont = currentFont.Family.CreateFont(currentFont.Size - 1);
        }

        return currentFont;
    }
}
