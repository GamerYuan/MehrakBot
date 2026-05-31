namespace Mehrak.Application.Shared.Renderers.Extensions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors;
using SixLabors.ImageSharp.Processing.Processors.Transforms;


public static class CropTransparentPixelsExtensions
{
    /// <summary>
    /// Crops the image to the bounding rectangle of all non-transparent pixels.
    /// </summary>
    public static IImageProcessingContext CropTransparentPixels(this IImageProcessingContext context)
    {
        return context.ApplyProcessor(new CropTransparentPixelsProcessor());
    }
}

/// <summary>
/// Defines a processor that detects non-transparent pixels and crops the image.
/// </summary>
public class CropTransparentPixelsProcessor : IImageProcessor
{
    public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(
        Configuration configuration,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        return new CropTransparentPixelsProcessor<TPixel>(configuration, source, sourceRectangle);
    }
}

/// <summary>
/// The internal worker that performs the pixel inspection and cropping.
/// </summary>
public sealed class CropTransparentPixelsProcessor<TPixel> : IImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration m_Configuration;
    private readonly Image<TPixel> m_Source;
    private readonly Rectangle m_SourceRectangle;

    public CropTransparentPixelsProcessor(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
    {
        m_Configuration = configuration;
        m_Source = source;
        m_SourceRectangle = sourceRectangle;
    }

    public void Execute()
    {
        var bounds = GetContentBounds(m_Source, m_SourceRectangle);

        // If the image is entirely transparent, we might choose to return a 1x1 empty image 
        // or the original. Here we'll just return to avoid errors, or crop to 1x1.
        if (bounds.IsEmpty)
        {
            return;
        }

        // Use the built-in CropProcessor to perform the actual crop resize operation.
        // This safely handles the mutation of the source image's frames.
        var cropProcessor = new CropProcessor(bounds, m_Source.Size);
        var cropWorker = cropProcessor.CreatePixelSpecificCloningProcessor(m_Configuration, m_Source, m_SourceRectangle);
        cropWorker.Execute();
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    private static Rectangle GetContentBounds(Image<TPixel> image, Rectangle sourceRectangle)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        const float alphaThreshold = 0.2f;

        sourceRectangle = Rectangle.Intersect(sourceRectangle, new Rectangle(0, 0, image.Width, image.Height));
        if (sourceRectangle.IsEmpty)
            return Rectangle.Empty;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = sourceRectangle.Y; y < sourceRectangle.Y + sourceRectangle.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = sourceRectangle.X; x < sourceRectangle.X + sourceRectangle.Width; x++)
                {
                    var rgba = row[x].ToScaledVector4();
                    if (rgba.W > alphaThreshold)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
        });

        if (minX > maxX || minY > maxY)
            return Rectangle.Empty;

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}


