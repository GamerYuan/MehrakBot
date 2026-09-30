using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Mehrak.Application.Shared.Renderers.Extensions;

internal static class LayerBoundsUtility
{
    internal const float AntialiasingFringe = 2f;

    public static int SaveLayer(DrawingCanvas canvas, params RectangleF[] localBounds)
    {
        return TryGetIntegerBounds(localBounds, out var bounds)
            ? canvas.SaveLayer(new GraphicsOptions(), bounds)
            : canvas.SaveLayer();
    }

    public static int SaveLayer(DrawingCanvas canvas, GraphicsOptions options, params RectangleF[] localBounds)
    {
        return TryGetIntegerBounds(localBounds, out var bounds)
            ? canvas.SaveLayer(options, bounds)
            : canvas.SaveLayer(options);
    }

    public static RectangleF GetTextBounds(
        string text,
        RichTextOptions options,
        float fringe = AntialiasingFringe,
        float additionalExtent = 0f)
    {
        if (!IsFinite(options.Origin) || !float.IsFinite(additionalExtent) || additionalExtent < 0)
            return InvalidBounds;

        if (HasUnsupportedDecorations(options))
            return InvalidBounds;

        if (string.IsNullOrEmpty(text))
            return Inflate(new RectangleF(options.Origin.X, options.Origin.Y, 1, 1), fringe + additionalExtent);

        var measured = TextMeasurer.MeasureBounds(text, options);
        var bounds = new RectangleF(measured.X, measured.Y, measured.Width, measured.Height);
        if (!IsFinite(bounds))
            return InvalidBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = new RectangleF(options.Origin.X, options.Origin.Y, 1, 1);

        var outlineExtent = GetTextOutlineExtent(options);
        return float.IsFinite(outlineExtent)
            ? Inflate(bounds, fringe + outlineExtent + additionalExtent)
            : InvalidBounds;
    }

    public static RectangleF Inflate(RectangleF bounds, float amount)
    {
        if (!IsFinite(bounds) || !float.IsFinite(amount) || amount < 0)
            return InvalidBounds;

        return new RectangleF(
            bounds.X - amount,
            bounds.Y - amount,
            bounds.Width + amount * 2,
            bounds.Height + amount * 2);
    }

    private static bool TryGetIntegerBounds(IEnumerable<RectangleF> bounds, out Rectangle result)
    {
        var hasBounds = false;
        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;

        foreach (var current in bounds)
        {
            if (!IsFinite(current) || current.Width < 0 || current.Height < 0)
            {
                result = default;
                return false;
            }

            if (!hasBounds)
            {
                left = current.Left;
                top = current.Top;
                right = current.Right;
                bottom = current.Bottom;
                hasBounds = true;
            }
            else
            {
                left = Math.Min(left, current.Left);
                top = Math.Min(top, current.Top);
                right = Math.Max(right, current.Right);
                bottom = Math.Max(bottom, current.Bottom);
            }
        }

        if (!hasBounds)
        {
            result = default;
            return false;
        }

        left = Math.Floor(left);
        top = Math.Floor(top);
        right = Math.Ceiling(right);
        bottom = Math.Ceiling(bottom);

        var width = right - left;
        var height = bottom - top;
        if (left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue
            || width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            result = default;
            return false;
        }

        result = new Rectangle((int)left, (int)top, (int)width, (int)height);
        return true;
    }

    private static bool HasUnsupportedDecorations(RichTextOptions options)
        => options.TextRuns.Any(run => run.StrikeoutPen != null || run.UnderlinePen != null || run.OverlinePen != null);

    private static float GetTextOutlineExtent(RichTextOptions options)
    {
        var maximumStrokeWidth = 0f;
        foreach (var run in options.TextRuns)
        {
            var pen = run.Pen;
            if (pen == null)
                continue;

            var strokeWidth = pen.StrokeWidth;
            if (!float.IsFinite(strokeWidth) || strokeWidth < 0)
                return float.NaN;

            maximumStrokeWidth = Math.Max(maximumStrokeWidth, strokeWidth);
        }

        return maximumStrokeWidth / 2f;
    }

    private static bool IsFinite(PointF point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static bool IsFinite(RectangleF bounds)
        => float.IsFinite(bounds.X) && float.IsFinite(bounds.Y)
           && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height);

    private static RectangleF InvalidBounds => new(float.NaN, float.NaN, float.NaN, float.NaN);
}
