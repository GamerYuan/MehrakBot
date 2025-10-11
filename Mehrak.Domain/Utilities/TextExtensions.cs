using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Domain.Utilities;

public static class TextExtensions
{
    public static void DrawFauxItalicText(this IImageProcessingContext context,
        string text,
        Font font,
        Color color,
        PointF location,
        float skewAngle = 15f)
    {
        // Create affine transform builder for more complex transformations
        AffineTransformBuilder transformBuilder = new AffineTransformBuilder()
            .AppendTranslation(new PointF(-location.X, -location.Y))  // Move to origin
            .AppendSkewDegrees(-skewAngle, 0)              // Skew along X axis
            .AppendTranslation(new PointF(location.X, location.Y));   // Move back

        FontRectangle textSize = TextMeasurer.MeasureSize(text, new RichTextOptions(font)
        {
            Origin = location
        });

        DrawingOptions drawingOptions = new()
        {
            Transform = transformBuilder.BuildMatrix(new Size((int)textSize.Width, (int)textSize.Height))
        };

        context.DrawText(drawingOptions, text, font, color, location);
    }

    public static void DrawFauxItalicText(this IImageProcessingContext context,
        RichTextOptions option,
        string text,
        Color color,
        float skewAngle = 15f)
    {
        AffineTransformBuilder transformBuilder = new AffineTransformBuilder()
            .AppendTranslation(new PointF(-option.Origin.X, -option.Origin.Y))
            .AppendSkewDegrees(-skewAngle, 0)
            .AppendTranslation(new PointF(option.Origin.X, option.Origin.Y));

        FontRectangle textSize = TextMeasurer.MeasureSize(text, option);

        DrawingOptions drawingOptions = new()
        {
            Transform = transformBuilder.BuildMatrix(new Size((int)textSize.Width, (int)textSize.Height)),
        };

        context.DrawText(drawingOptions, option, text, new SolidBrush(color), new SolidPen(Color.Transparent));
    }
}
