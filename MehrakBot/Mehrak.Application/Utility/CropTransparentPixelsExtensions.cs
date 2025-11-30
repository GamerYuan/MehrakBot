namespace Mehrak.Application.Utility;

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
        var bounds = CropTransparentPixelsProcessor<TPixel>.GetContentBounds(m_Source);

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

    private static Rectangle GetContentBounds(Image<TPixel> image)
    {
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        var minX = int.MaxValue;
        var maxX = int.MinValue;

        // 1. Find vertical bounds (minY, maxY)
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);

                // Check if this row has any non-transparent pixel
                var hasContent = false;
                for (var x = 0; x < row.Length; x++)
                {
                    // Check alpha channel. 
                    // Note: ToRgba32() is reasonably fast, but for extreme performance 
                    // on specific types you might optimize this.
                    if (row[x].ToVector4().W > 0)
                    {
                        hasContent = true;
                        break;
                    }
                }

                if (hasContent)
                {
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        });

        // If no content found, return empty
        if (minY == int.MaxValue)
        {
            return Rectangle.Empty;
        }

        // 2. Find horizontal bounds (minX, maxX) strictly within the vertical bounds
        image.ProcessPixelRows(accessor =>
        {
            for (var y = minY; y <= maxY; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].ToVector4().W > 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                    }
                }
            }
        });

        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}

