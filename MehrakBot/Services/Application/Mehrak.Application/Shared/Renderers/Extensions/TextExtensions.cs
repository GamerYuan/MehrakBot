#region

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public static class TextExtensions
{
    public static void DrawFauxItalicText(
        this DrawingCanvas canvas,
        string text,
        Font font,
        Color color,
        PointF location,
        float skewAngle = 15f)
    {
        // Create affine transform builder for more complex transformations
        var transformBuilder = new AffineTransformBuilder()
            .AppendTranslation(new PointF(-location.X, -location.Y)) // Move to origin
            .AppendSkewDegrees(-skewAngle, 0) // Skew along X axis
            .AppendTranslation(new PointF(location.X, location.Y)); // Move back

        var textSize = TextMeasurer.MeasureBounds(text, new RichTextOptions(font)
        {
            Origin = location
        });

        DrawingOptions drawingOptions = new()
        {
            Transform = new(transformBuilder.BuildMatrix(new Size((int)textSize.Width, (int)textSize.Height)))
        };
        _ = canvas.Save(drawingOptions);
        canvas.DrawText(new RichTextOptions(font) { Origin = location }, text, Brushes.Solid(color), null);
        canvas.Restore();
    }

    public static void DrawFauxItalicText(
        this DrawingCanvas canvas,
        RichTextOptions option,
        string text,
        Color color,
        float skewAngle = 15f)
    {
        var transformBuilder = new AffineTransformBuilder()
            .AppendTranslation(new PointF(-option.Origin.X, -option.Origin.Y))
            .AppendSkewDegrees(-skewAngle, 0)
            .AppendTranslation(new PointF(option.Origin.X, option.Origin.Y));

        var textSize = TextMeasurer.MeasureBounds(text, option);

        DrawingOptions drawingOptions = new()
        {
            Transform = new(transformBuilder.BuildMatrix(new Size((int)textSize.Width, (int)textSize.Height)))
        };

        _ = canvas.Save(drawingOptions);
        canvas.DrawText(option, text, Brushes.Solid(color), null);
        canvas.Restore();
    }
}
