#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Utility;

public static class ImageUtility
{
    private static readonly Color StarColor = Color.ParseHex("#FFCC33");
    private static readonly Color ShadowColor = new(new Rgba32(0, 0, 0, 100)); // Semi-transparent black for shadow

    /// <summary>
    /// Applies a horizontal gradient fade to an image, making it gradually
    /// transparent towards the right
    /// </summary>
    public static IImageProcessingContext ApplyGradientFade(this IImageProcessingContext context,
        float fadeStart = 0.75f)
    {
        return context.ProcessPixelRowsAsVector4(row =>
        {
            var width = row.Length;
            var fadeStartX = (int)(width * fadeStart);
            // fade only columns from fadeStartX → width
            for (var x = fadeStartX; x < width; x++)
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
        var width = 5 * starSize;
        var height = starSize;

        var centerY = starSize / 2;
        var offset = (5 - starCount) * starSize / 2;

        var image = new Image<Rgba32>(width, height);

        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (var i = 0; i < starCount; i++)
            {
                var centerX = offset + i * starSize + starSize / 2;

                // Create a star shape
                PointF[] points = CreateStarPoints(centerX, centerY, (float)starSize / 2, (float)starSize / 4, 5);
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

            for (var i = 0; i < starCount; i++)
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
                    PointF[] shadowPoints = CreateStarPoints(
                        centerX,
                        centerY,
                        (float)starSize / 2 * shadowExpansion, // Larger outer radius
                        (float)starSize / 4 * shadowExpansion, // Larger inner radius
                        4);
                    var shadowPolygon = new Polygon(shadowPoints);
                    ctx.Fill(ShadowColor, shadowPolygon);
                }

                // Then create the actual star on top
                PointF[] starPoints = CreateStarPoints(centerX, centerY, (float)starSize / 2, (float)starSize / 4, 4);
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

            // Enforces that any part of this shape that has color is punched
            // out of the background
            AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
        });

        // Mutating in here as we already have a cloned original use any color
        // (not Transparent), so the corners will be clipped
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
        var angle = -MathF.PI / 2;
        var angleIncrement = MathF.PI / points;

        for (var i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outerRadius : innerRadius;
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

        // Corner is now a corner shape positions top left let's make 3 more
        // positioned correctly, we can do that by translating the original
        // around the center of the image.

        var rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
        var bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

        // Move it across the width of the image - the width of the shape
        IPath cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
        IPath cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
        IPath cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

        return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
    }

    public readonly struct ImagePosition(int x, int y, int imageIndex)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public int ImageIndex { get; } = imageIndex;
    }

    public class GridLayout
    {
        public int OutputWidth { get; set; }
        public int OutputHeight { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public int[] Padding { get; }
        public int ImageSpacing { get; set; }
        public List<ImagePosition> ImagePositions { get; set; }

        public GridLayout(int[] padding)
        {
            Padding = padding;
            ImagePositions = [];
        }

        // Helper properties for easier access to padding values
        public int PaddingTop => Padding[0];
        public int PaddingRight => Padding[1];
        public int PaddingBottom => Padding[2];
        public int PaddingLeft => Padding[3];
    }

    public static GridLayout CalculateGridLayout(int imageCount,
        int imageWidth,
        int imageHeight,
        int[] padding,
        int imageSpacing = 20)
    {
        if (imageCount <= 0)
            throw new ArgumentException("Image count must be greater than 0");

        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentException("Image dimensions must be greater than 0");

        if (padding == null || padding.Length != 4)
            throw new ArgumentException("Padding array must contain exactly 4 values: top, right, bottom, left");

        if (padding.Any(p => p < 0))
            throw new ArgumentException("Padding values cannot be negative");

        if (imageSpacing < 0)
            throw new ArgumentException("Image spacing cannot be negative");

        var layout = new GridLayout((int[])padding.Clone())
        {
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            ImageSpacing = imageSpacing
        };

        // Find the best grid dimensions (columns x rows) that gives us closest
        // to 16:9 ratio
        (int columns, int rows) bestLayout = FindOptimalGridDimensions(imageCount, imageWidth, imageHeight, padding, imageSpacing);
        layout.Columns = bestLayout.columns;
        layout.Rows = bestLayout.rows;

        // Calculate output image dimensions
        layout.OutputWidth = layout.PaddingLeft + layout.PaddingRight +
                             layout.Columns * imageWidth +
                             (layout.Columns - 1) * imageSpacing;

        layout.OutputHeight = layout.PaddingTop + layout.PaddingBottom +
                              layout.Rows * imageHeight +
                              (layout.Rows - 1) * imageSpacing;

        // Calculate positions for each image
        layout.ImagePositions = CalculateImagePositions(layout.Columns, layout.Rows, imageCount,
            imageWidth, imageHeight, padding, imageSpacing);

        return layout;
    }

    private static (int columns, int rows) FindOptimalGridDimensions(int imageCount,
        int imageWidth,
        int imageHeight,
        int[] padding,
        int imageSpacing)
    {
        var bestColumns = 1;
        var bestRows = imageCount;
        var bestRatioDifference = double.MaxValue;

        // Try different column configurations
        for (var columns = 1; columns <= imageCount; columns++)
        {
            var rows = (int)Math.Ceiling((double)imageCount / columns);

            // Calculate what the aspect ratio would be with this grid
            var gridWidth = padding[3] + padding[1] + // left + right padding
                            columns * imageWidth +
                            (columns - 1) * imageSpacing;

            var gridHeight = padding[0] + padding[2] + // top + bottom padding
                             rows * imageHeight +
                             (rows - 1) * imageSpacing;

            var currentRatio = (double)gridWidth / gridHeight;
            var ratioDifference = Math.Abs(currentRatio - 4.0 / 3.0);

            if (ratioDifference < bestRatioDifference)
            {
                bestRatioDifference = ratioDifference;
                bestColumns = columns;
                bestRows = rows;
            }
        }

        return (bestColumns, bestRows);
    }

    private static List<ImagePosition> CalculateImagePositions(int columns,
        int rows,
        int imageCount,
        int imageWidth,
        int imageHeight,
        int[] padding,
        int imageSpacing)
    {
        var positions = new List<ImagePosition>();

        for (var i = 0; i < imageCount; i++)
        {
            var column = i % columns;
            var row = i / columns;

            var x = padding[3] + column * (imageWidth + imageSpacing); // left padding + column offset
            var y = padding[0] + row * (imageHeight + imageSpacing); // top padding + row offset

            positions.Add(new ImagePosition(x, y, i));
        }

        return positions;
    }
}