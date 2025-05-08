#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Utility;

public static class ImageExtensions
{
    public static Image<Rgba32> StandardizeImageSize(Image<Rgba32> image, int size)
    {
        int width = image.Width;
        int height = image.Height;

        // If image is already 1280x1280, no need to resize
        if (width == size && height == size)
            return image;

        // If image dimensions exceed 1280x1280 or are smaller than 1280x1280,
        // resize so the longest side is 1280 while maintaining aspect ratio
        if (width > size || height > size ||
            (width < size && height < size))
        {
            float aspectRatio = (float)width / height;
            int newWidth, newHeight;

            if (width >= height)
            {
                newWidth = size;
                newHeight = (int)(size / aspectRatio);
            }
            else
            {
                newHeight = size;
                newWidth = (int)(size * aspectRatio);
            }

            image.Mutate(x => x.Resize(newWidth, newHeight));
            width = newWidth;
            height = newHeight;
        }

        // If one dimension is already 1280 and the other isn't,
        // or if we need to pad the resized image,
        // create a new 1280x1280 image with the original centered
        if (width != size || height != size)
        {
            var centeredImage = new Image<Rgba32>(size, size);
            centeredImage.Mutate(x => x.BackgroundColor(new Rgba32(0, 0, 0, 0))); // Transparent background

            int x = (size - width) / 2;
            int y = (size - height) / 2;

            centeredImage.Mutate(ctx => ctx.DrawImage(image, new Point(x, y), 1f));

            image.Dispose();
            return centeredImage;
        }

        return image;
    }

    /// <summary>
    /// Applies a horizontal gradient fade to an image, making it gradually transparent towards the right
    /// </summary>
    public static IImageProcessingContext ApplyGradientFade(this IImageProcessingContext context,
        float fadeStart = 0.75f)
    {
        var size = context.GetCurrentSize();

        return context.ProcessPixelRowsAsVector4(row =>
        {
            int width = row.Length;
            int fadeStartX = (int)(width * fadeStart);
            // fade only columns from fadeStartX → width
            for (int x = fadeStartX; x < width; x++)
            {
                // same fall‑off curve you had before
                double alpha = 1.0
                               - (double)(x - fadeStartX)
                               / (width - fadeStartX);
                alpha = Math.Pow(alpha, 5);
                alpha = Math.Clamp(alpha, 0, 1);

                // apply to the existing alpha
                row[x].W *= (float)alpha;
            }
        });
    }

    public static Image<Rgba32> GenerateStarRating(int starCount)
    {
        starCount = Math.Clamp(starCount, 1, 5);

        const int starSize = 30;
        int width = 5 * starSize;
        int height = starSize;

        int centerY = starSize / 2;
        var starColor = Color.Yellow;
        var offset = (5 - starCount) * starSize / 2;

        var image = new Image<Rgba32>(width, height);

        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < starCount; i++)
            {
                int centerX = offset + i * starSize + starSize / 2;

                // Create a star shape
                var points = CreateStarPoints(centerX, centerY, (float)starSize / 2, (float)starSize / 4, 5);
                var starPolygon = new Polygon(points);

                ctx.Fill(starColor, starPolygon);
            }
        });
        return image;
    }

    public static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context, float cornerRadius)
    {
        Size size = context.GetCurrentSize();
        IPathCollection corners = BuildCorners(size.Width, size.Height, cornerRadius);

        context.SetGraphicsOptions(new GraphicsOptions
        {
            Antialias = true,

            // Enforces that any part of this shape that has color is punched out of the background
            AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
        });

        // Mutating in here as we already have a cloned original
        // use any color (not Transparent), so the corners will be clipped
        foreach (IPath path in corners) context = context.Fill(Color.Red, path);

        return context;
    }

    private static PointF[] CreateStarPoints(float centerX, float centerY, float outerRadius, float innerRadius,
        int points)
    {
        var result = new PointF[points * 2];
        float angle = -MathF.PI / 2;
        float angleIncrement = MathF.PI / points;

        for (int i = 0; i < points * 2; i++)
        {
            float radius = i % 2 == 0 ? outerRadius : innerRadius;
            result[i] = new PointF(
                centerX + radius * MathF.Cos(angle),
                centerY + radius * MathF.Sin(angle)
            );
            angle += angleIncrement;
        }

        return result;
    }

    private static PathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
    {
        // First create a square
        var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

        // Then cut out of the square a circle so we are left with a corner
        IPath cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

        // Corner is now a corner shape positions top left
        // let's make 3 more positioned correctly, we can do that by translating the original around the center of the image.

        float rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
        float bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

        // Move it across the width of the image - the width of the shape
        IPath cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
        IPath cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
        IPath cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

        return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
    }
}
