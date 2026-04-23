#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Renderers.Extensions;

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
    public static IImageProcessingContext DrawStatLine(
        this IImageProcessingContext ctx,
        StatLineData data,
        StatLineStyle style,
        PointF position,
        float width)
    {
        var hasBreakdown = style.BreakdownFont != null
                           && !string.IsNullOrEmpty(data.BaseValue) && !string.IsNullOrEmpty(data.BonusValue);

        var rightAlignX = position.X + width;
        var iconTop = position.Y;
        var mainCenterY = iconTop + IconSize * 0.5f;
        var breakdownCenterY = mainCenterY;
        var valueCenterY = mainCenterY;

        if (hasBreakdown)
        {
            valueCenterY = iconTop + IconSize * 0.2f;
            breakdownCenterY = iconTop + IconSize * 0.9f;
        }

        // Icon (or reserved spacing)
        var nameX = position.X + IconSize + IconTextSpacing;

        if (style.StatIcon != null)
        {
            ctx.DrawImage(style.StatIcon, new Point((int)position.X, (int)iconTop), 1f);
        }

        // Measure value to determine how much space the name can use
        var maxNameWidth = width * MaxNameWidthRatio;
        if (maxNameWidth < 10) maxNameWidth = 10;

        // Stat name — try wrapping first to keep font larger, fall back to shrinking if too tall
        var nameOptions = new RichTextOptions(style.Font)
        {
            Origin = new PointF(nameX, 0),
            VerticalAlignment = VerticalAlignment.Top
        };

        var nameSize = TextMeasurer.MeasureSize(data.StatName, nameOptions);

        if (nameSize.Width > maxNameWidth)
        {
            nameOptions.WrappingLength = maxNameWidth;
            nameSize = TextMeasurer.MeasureSize(data.StatName, nameOptions);

            // If wrapping makes it too tall, fall back to shrinking font on a single line
            if (nameSize.Height > IconSize * 1.25f)
            {
                var shrunkFont = GetFittingFont(data.StatName, style.Font, maxNameWidth);
                nameOptions = new RichTextOptions(shrunkFont)
                {
                    Origin = new PointF(nameX, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
            }
        }

        nameOptions.Origin = new PointF(nameX, GetVisualCenterY(data.StatName, nameOptions, mainCenterY));
        ctx.DrawText(nameOptions, data.StatName, style.TextColor);

        // Final value (right aligned)
        ctx.DrawText(new RichTextOptions(style.Font)
        {
            Origin = new PointF(rightAlignX, GetVisualCenterY(data.FinalValue, style.Font, valueCenterY)),
            HorizontalAlignment = HorizontalAlignment.Right
        }, data.FinalValue, style.TextColor);

        // Breakdown (base + bonus)
        if (hasBreakdown)
        {
            var breakdownFont = style.BreakdownFont!;
            var baseColor = style.BaseColor ?? Color.LightGray;
            var bonusColor = style.BonusColor ?? Color.LightGreen;

            var baseTextX = rightAlignX;

            if (!string.IsNullOrEmpty(data.BonusValue))
            {
                var bonusSize = TextMeasurer.MeasureSize(data.BonusValue, new RichTextOptions(breakdownFont));
                ctx.DrawText(new RichTextOptions(breakdownFont)
                {
                    Origin = new PointF(rightAlignX, GetVisualCenterY(data.BonusValue, breakdownFont, breakdownCenterY)),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, data.BonusValue, bonusColor);

                baseTextX = rightAlignX - bonusSize.Width - BreakdownGap;
            }

            if (!string.IsNullOrEmpty(data.BaseValue))
            {
                ctx.DrawText(new RichTextOptions(breakdownFont)
                {
                    Origin = new PointF(baseTextX, GetVisualCenterY(data.BaseValue, breakdownFont, breakdownCenterY)),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, data.BaseValue, baseColor);
            }
        }

        return ctx;
    }

    /// <summary>
    /// Computes the Y coordinate that will visually center the actual glyph bounds
    /// (not the font metrics box) at the given target center Y.
    /// </summary>
    private static float GetVisualCenterY(string text, Font font, float targetCenterY)
    {
        var size = TextMeasurer.MeasureSize(text, new RichTextOptions(font)
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
        var size = TextMeasurer.MeasureSize(text, measureOptions);
        return targetCenterY - size.Height / 2f;
    }

    private static Font GetFittingFont(string text, Font font, float maxWidth)
    {
        var currentFont = font;
        var size = TextMeasurer.MeasureSize(text, new RichTextOptions(currentFont));

        while (size.Width > maxWidth && currentFont.Size > 10)
        {
            currentFont = currentFont.Family.CreateFont(currentFont.Size - 1);
            size = TextMeasurer.MeasureSize(text, new RichTextOptions(currentFont));
        }

        return currentFont;
    }
}
