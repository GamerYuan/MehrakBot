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
    /// <summary>
    /// Applies a horizontal gradient fade to an image, making it gradually transparent towards the right
    /// </summary>
    public static void ApplyGradientFade(this Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;

        int fadeStartX = Math.Max(0, (int)(width * 0.75f)); // Start fading from this position in the image

        // Apply a gradient mask from right to left
        for (int x = fadeStartX; x < width; x++)
        {
            double alpha = 1.0f - (double)(x - fadeStartX) / (width - fadeStartX);
            alpha = Math.Pow(alpha, 5);
            alpha = Math.Max(0, Math.Min(1, alpha)); // Clamp between 0 and 1

            for (int y = 0; y < height; y++)
            {
                var pixel = image[x, y];
                image[x, y] = new Rgba32(
                    pixel.R,
                    pixel.G,
                    pixel.B,
                    (byte)(pixel.A * alpha)
                );
            }
        }
    }

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

    public static Image<Rgba32> GenerateStarRating(int starCount)
    {
        starCount = Math.Clamp(starCount, 1, 5);

        const int starSize = 30;
        const int spacing = 5;
        int width = 5 * starSize + 4 * spacing;
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
                int centerX = offset + i * (starSize + spacing) + starSize / 2;

                // Create a star shape
                var points = CreateStarPoints(centerX, centerY, (float)starSize / 2, (float)starSize / 4, 5);
                var starPolygon = new Polygon(points);

                ctx.Fill(starColor, starPolygon);
            }
        });
        return image;
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
}
