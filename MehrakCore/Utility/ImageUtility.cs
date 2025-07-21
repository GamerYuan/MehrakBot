#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Utility;

public static class ImageUtility
{
    private static readonly Color StarColor = Color.ParseHex("#FFCC33");
    private static readonly Color ShadowColor = new(new Rgba32(0, 0, 0, 100)); // Semi-transparent black for shadow

    /// <summary>
    /// Applies a horizontal gradient fade to an image, making it gradually transparent towards the right
    /// </summary>
    public static IImageProcessingContext ApplyGradientFade(this IImageProcessingContext context,
        float fadeStart = 0.75f)
    {
        return context.ProcessPixelRowsAsVector4(row =>
        {
            int width = row.Length;
            int fadeStartX = (int)(width * fadeStart);
            // fade only columns from fadeStartX → width
            for (int x = fadeStartX; x < width; x++)
            {
                // same fall‑off curve you had before
                var alpha = 1.0f - (float)(x - fadeStartX) / (width - fadeStartX);
                alpha = MathF.Pow(alpha, 5);
                alpha = Math.Clamp(alpha, 0, 1);

                // apply to the existing alpha
                row[x].W *= alpha;
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

                ctx.Fill(StarColor, starPolygon);
            }
        });
        return image;
    }

    public static Image<Rgba32> GenerateFourSidedStarRating(int starCount, bool isHorizontal = true,
        bool drawShadow = true)
    {
        starCount = Math.Clamp(starCount, 1, 5);

        const int starSize = 30;
        const float shadowExpansion = 1.3f; // Shadow is 20% larger than the star

        // Determine dimensions based on orientation
        int width, height;
        if (isHorizontal)
        {
            width = 5 * starSize;
            height = starSize;
        }
        else
        {
            width = starSize;
            height = 5 * starSize;
        }

        var image = new Image<Rgba32>(width, height);

        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < starCount; i++)
            {
                float centerX, centerY;

                if (isHorizontal)
                {
                    centerX = i * starSize + 15; // Left-aligned horizontally
                    centerY = 15f;
                }
                else
                {
                    centerX = 15f;
                    centerY = i * starSize + 15; // Top-aligned vertically
                }

                // Draw shadow only if requested
                if (drawShadow)
                {
                    // Create shadow first (expanded on all sides)
                    var shadowPoints = CreateStarPoints(
                        centerX,
                        centerY,
                        (float)starSize / 2 * shadowExpansion, // Larger outer radius
                        (float)starSize / 4 * shadowExpansion, // Larger inner radius
                        4);
                    var shadowPolygon = new Polygon(shadowPoints);
                    ctx.Fill(ShadowColor, shadowPolygon);
                }

                // Then create the actual star on top
                var starPoints = CreateStarPoints(centerX, centerY, (float)starSize / 2, (float)starSize / 4, 4);
                var starPolygon = new Polygon(starPoints);
                ctx.Fill(StarColor, starPolygon);
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

    public static IPath CreateRoundedRectanglePath(int width, int height, float cornerRadius)
    {
        var pathBuilder = new PathBuilder();
        width--;
        height--;

        var radius = 2 * cornerRadius;

        // Make sure the rounded corners are no larger than half the size of the rectangle
        cornerRadius = Math.Min(width * 0.5f, Math.Min(height * 0.5f, cornerRadius));

        // Start drawing path
        pathBuilder.StartFigure();

        // upperBorder
        pathBuilder.AddLine(cornerRadius, 0, width - cornerRadius, 0);

        // Upper right rounded corner
        pathBuilder.AddArc(new RectangleF(width - radius, 0, radius, radius), 0, 270, 90);

        // right line
        pathBuilder.AddLine(width, cornerRadius, width, height - cornerRadius);

        // Lower right rounded corner
        pathBuilder.AddArc(new RectangleF(width - radius, height - radius, radius, radius), 0, 0, 90);

        // lower border
        pathBuilder.AddLine(width - cornerRadius, height, cornerRadius, height);

        // Lower left rounded corner
        pathBuilder.AddArc(new RectangleF(0, height - radius, radius, radius), 0, 90, 90);

        // left line
        pathBuilder.AddLine(0, height - cornerRadius, 0, cornerRadius);

        // Upper left rounded corner
        pathBuilder.AddArc(new RectangleF(0, 0, radius, radius), 0, 180, 90);

        // Close the path to form a complete rectangle
        pathBuilder.CloseFigure();

        return pathBuilder.Build();
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
