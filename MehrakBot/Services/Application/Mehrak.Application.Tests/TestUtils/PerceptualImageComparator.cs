using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Tests.TestUtils;

/// <summary>
/// Bounded perceptual comparison for rendered-card regression tests. The global DCT hash tolerates
/// small layout and encoding changes; color and structure maps prevent equal-luminance color changes
/// and localized omissions from being hidden by that coarse hash.
/// </summary>
internal static class PerceptualImageComparator
{
    internal const double DefaultDctSimilarityPercent = 82.0;

    // These fixed guardrails deliberately remain independent of the caller's DCT threshold.
    // Raising/lowering the explicit threshold only changes the global composition requirement.
    internal const double MinimumGlobalColorSimilarityPercent = 94.0;
    internal const double MinimumGlobalStructureSimilarityPercent = 90.0;
    internal const double MinimumLocalSimilarityPercent = 96.0;
    private const int HashSampleSize = 32;
    private const int HashCoefficientSize = 8;
    private const int LocalSearchRadius = 2;
    private const int RegionSize = 16;
    private const int MaximumAnalysisDimension = 1024;
    private const int MaximumAnalysisPixels = 131_072;
    private const int ReportedRegionCount = 3;
    private const int LocalRegionAggregateCount = 4;

    internal static PerceptualComparison Compare(byte[] expectedBytes, byte[] actualBytes,
        double minimumDctSimilarityPercent)
    {
        ValidateThreshold(minimumDctSimilarityPercent);

        using var expected = LoadOriented(expectedBytes);
        using var actual = LoadOriented(actualBytes);

        if (expected.Size != actual.Size)
        {
            return PerceptualComparison.DimensionMismatch(expected.Width, expected.Height,
                actual.Width, actual.Height, minimumDctSimilarityPercent);
        }

        var expectedHash = ComputeDctHash(expected);
        var actualHash = ComputeDctHash(actual);
        var dctSimilarity = (63 - BitOperations.PopCount(expectedHash ^ actualHash)) / 63.0 * 100.0;

        var analysisSize = GetAnalysisSize(expected.Width, expected.Height);
        using var expectedAnalysis = expected.Clone(context => context.Resize(analysisSize.Width, analysisSize.Height));
        using var actualAnalysis = actual.Clone(context => context.Resize(analysisSize.Width, analysisSize.Height));

        var expectedFeatures = FeatureMap.Create(expectedAnalysis);
        var actualFeatures = FeatureMap.Create(actualAnalysis);
        var regionalScores = CompareFeatures(expectedFeatures, actualFeatures, expected.Width, expected.Height);

        var globalColorSimilarity = 100.0 * (1.0 - regionalScores.ColorMismatch);
        var globalStructureSimilarity = 100.0 * (1.0 - regionalScores.StructureMismatch);
        // Averaging the four worst bounded regions detects a missing icon or label while avoiding a
        // pixel-identity requirement. Deliberately, this does not promise detection of every isolated
        // one-pixel glyph change; tightening it enough to do so would reject the accepted shifts.
        var localSimilarity = 100.0 * (1.0 - regionalScores.WorstRegionMismatch);
        var isEquivalent = dctSimilarity >= minimumDctSimilarityPercent
                           && globalColorSimilarity >= MinimumGlobalColorSimilarityPercent
                           && globalStructureSimilarity >= MinimumGlobalStructureSimilarityPercent
                           && localSimilarity >= MinimumLocalSimilarityPercent;

        return new PerceptualComparison(isEquivalent, expected.Width, expected.Height,
            actual.Width, actual.Height, dctSimilarity, minimumDctSimilarityPercent,
            globalColorSimilarity, globalStructureSimilarity, localSimilarity,
            regionalScores.WorstRegions);
    }

    internal static void ValidateThreshold(double minimumDctSimilarityPercent)
    {
        if (!double.IsFinite(minimumDctSimilarityPercent)
            || minimumDctSimilarityPercent is < 0.0 or > 100.0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDctSimilarityPercent),
                minimumDctSimilarityPercent, "DCT similarity threshold must be finite and between 0 and 100 percent.");
        }
    }

    private static Image<Rgba32> LoadOriented(byte[] bytes)
    {
        var image = Image.Load<Rgba32>(bytes);
        image.Mutate(context => context.AutoOrient());
        return image;
    }

    private static Size GetAnalysisSize(int width, int height)
    {
        var pixelScale = Math.Sqrt(MaximumAnalysisPixels / (double)(width * (long)height));
        var dimensionScale = MaximumAnalysisDimension / (double)Math.Max(width, height);
        var scale = Math.Min(1.0, Math.Min(pixelScale, dimensionScale));
        return new Size(Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static ulong ComputeDctHash(Image<Rgba32> source)
    {
        using var sample = source.Clone(context => context.Resize(HashSampleSize, HashSampleSize));
        var luminance = new double[HashSampleSize * HashSampleSize];
        sample.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < HashSampleSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < HashSampleSize; x++)
                {
                    var pixel = row[x];
                    var alpha = pixel.A / 255.0;
                    luminance[y * HashSampleSize + x] = alpha
                        * (0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B) / 255.0;
                }
            }
        });

        var coefficients = new double[HashCoefficientSize * HashCoefficientSize - 1];
        var coefficientIndex = 0;
        for (var v = 0; v < HashCoefficientSize; v++)
        {
            for (var u = 0; u < HashCoefficientSize; u++)
            {
                if (u == 0 && v == 0)
                    continue;

                var coefficient = 0.0;
                for (var y = 0; y < HashSampleSize; y++)
                {
                    var verticalBasis = Math.Cos((2 * y + 1) * v * Math.PI / (2 * HashSampleSize));
                    for (var x = 0; x < HashSampleSize; x++)
                    {
                        var horizontalBasis = Math.Cos((2 * x + 1) * u * Math.PI / (2 * HashSampleSize));
                        coefficient += luminance[y * HashSampleSize + x] * horizontalBasis * verticalBasis;
                    }
                }

                coefficients[coefficientIndex++] = coefficient;
            }
        }

        var sortedCoefficients = (double[])coefficients.Clone();
        Array.Sort(sortedCoefficients);
        var median = sortedCoefficients[sortedCoefficients.Length / 2];
        ulong hash = 0;
        for (var index = 0; index < coefficients.Length; index++)
        {
            if (coefficients[index] >= median)
                hash |= 1UL << index;
        }

        return hash;
    }

    private static RegionalScores CompareFeatures(FeatureMap expected, FeatureMap actual,
        int sourceWidth, int sourceHeight)
    {
        var regionColumns = (expected.Width + RegionSize - 1) / RegionSize;
        var regionRows = (expected.Height + RegionSize - 1) / RegionSize;
        var regions = new RegionAccumulator[regionColumns * regionRows];

        AccumulateDirectionalMismatch(expected, actual, regions, regionColumns);
        AccumulateDirectionalMismatch(actual, expected, regions, regionColumns);

        var totalColorMismatch = 0.0;
        var totalStructureMismatch = 0.0;
        var totalSamples = 0;
        var reports = new List<RegionReport>(regions.Length);
        for (var index = 0; index < regions.Length; index++)
        {
            var region = regions[index];
            if (region.SampleCount == 0)
                continue;

            totalColorMismatch += region.ColorMismatch;
            totalStructureMismatch += region.StructureMismatch;
            totalSamples += region.SampleCount;

            var colorMismatch = region.ColorMismatch / region.SampleCount;
            var structureMismatch = region.StructureMismatch / region.SampleCount;
            var combinedMismatch = 0.65 * colorMismatch + 0.35 * structureMismatch;
            var regionX = index % regionColumns;
            var regionY = index / regionColumns;
            reports.Add(CreateRegionReport(regionX, regionY, expected.Width, expected.Height,
                sourceWidth, sourceHeight, colorMismatch, structureMismatch, combinedMismatch));
        }

        var worstRegions = reports
            .OrderByDescending(report => report.CombinedMismatch)
            .Take(ReportedRegionCount)
            .ToArray();
        var worstRegionMismatch = reports
            .OrderByDescending(report => report.CombinedMismatch)
            .Take(LocalRegionAggregateCount)
            .Average(report => report.CombinedMismatch);

        return new RegionalScores(totalColorMismatch / totalSamples,
            totalStructureMismatch / totalSamples, worstRegionMismatch, worstRegions);
    }

    private static void AccumulateDirectionalMismatch(FeatureMap source, FeatureMap target,
        RegionAccumulator[] regions, int regionColumns)
    {
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var minimumColorDistance = double.MaxValue;
                var minimumStructureDistance = double.MaxValue;
                var minimumY = Math.Max(0, y - LocalSearchRadius);
                var maximumY = Math.Min(source.Height - 1, y + LocalSearchRadius);
                var minimumX = Math.Max(0, x - LocalSearchRadius);
                var maximumX = Math.Min(source.Width - 1, x + LocalSearchRadius);

                for (var candidateY = minimumY; candidateY <= maximumY; candidateY++)
                {
                    for (var candidateX = minimumX; candidateX <= maximumX; candidateX++)
                    {
                        minimumColorDistance = Math.Min(minimumColorDistance,
                            source.ColorDistance(x, y, target, candidateX, candidateY));
                        minimumStructureDistance = Math.Min(minimumStructureDistance,
                            source.StructureDistance(x, y, target, candidateX, candidateY));
                    }
                }

                var regionIndex = y / RegionSize * regionColumns + x / RegionSize;
                regions[regionIndex].ColorMismatch += minimumColorDistance;
                regions[regionIndex].StructureMismatch += minimumStructureDistance;
                regions[regionIndex].SampleCount++;
            }
        }
    }

    private static RegionReport CreateRegionReport(int regionX, int regionY, int analysisWidth,
        int analysisHeight, int sourceWidth, int sourceHeight, double colorMismatch,
        double structureMismatch, double combinedMismatch)
    {
        var x = regionX * RegionSize * sourceWidth / analysisWidth;
        var y = regionY * RegionSize * sourceHeight / analysisHeight;
        var right = Math.Min(sourceWidth,
            (regionX + 1) * RegionSize * sourceWidth / analysisWidth);
        var bottom = Math.Min(sourceHeight,
            (regionY + 1) * RegionSize * sourceHeight / analysisHeight);
        return new RegionReport(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y),
            colorMismatch, structureMismatch, combinedMismatch);
    }

    private sealed class FeatureMap
    {
        private readonly float[] m_Colors;
        private readonly float[] m_HorizontalEdges;
        private readonly float[] m_VerticalEdges;

        private FeatureMap(int width, int height, float[] colors, float[] horizontalEdges,
            float[] verticalEdges)
        {
            Width = width;
            Height = height;
            m_Colors = colors;
            m_HorizontalEdges = horizontalEdges;
            m_VerticalEdges = verticalEdges;
        }

        internal int Width { get; }
        internal int Height { get; }

        internal static FeatureMap Create(Image<Rgba32> image)
        {
            var pixelCount = image.Width * image.Height;
            var colors = new float[pixelCount * 4];
            var luminance = new float[pixelCount];
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < image.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < image.Width; x++)
                    {
                        var pixel = row[x];
                        var pixelIndex = y * image.Width + x;
                        var colorIndex = pixelIndex * 4;
                        var alpha = pixel.A / 255f;
                        colors[colorIndex] = pixel.R / 255f * alpha;
                        colors[colorIndex + 1] = pixel.G / 255f * alpha;
                        colors[colorIndex + 2] = pixel.B / 255f * alpha;
                        colors[colorIndex + 3] = alpha;
                        luminance[pixelIndex] = alpha
                            * (0.2126f * pixel.R + 0.7152f * pixel.G + 0.0722f * pixel.B) / 255f;
                    }
                }
            });

            var horizontalEdges = new float[pixelCount];
            var verticalEdges = new float[pixelCount];
            for (var y = 0; y < image.Height; y++)
            {
                var previousY = Math.Max(0, y - 1);
                var nextY = Math.Min(image.Height - 1, y + 1);
                for (var x = 0; x < image.Width; x++)
                {
                    var previousX = Math.Max(0, x - 1);
                    var nextX = Math.Min(image.Width - 1, x + 1);
                    var index = y * image.Width + x;
                    horizontalEdges[index] = (luminance[y * image.Width + nextX]
                                              - luminance[y * image.Width + previousX]) / 2f;
                    verticalEdges[index] = (luminance[nextY * image.Width + x]
                                            - luminance[previousY * image.Width + x]) / 2f;
                }
            }

            return new FeatureMap(image.Width, image.Height, colors, horizontalEdges, verticalEdges);
        }

        internal double ColorDistance(int x, int y, FeatureMap other, int otherX, int otherY)
        {
            var index = (y * Width + x) * 4;
            var otherIndex = (otherY * other.Width + otherX) * 4;
            var red = m_Colors[index] - other.m_Colors[otherIndex];
            var green = m_Colors[index + 1] - other.m_Colors[otherIndex + 1];
            var blue = m_Colors[index + 2] - other.m_Colors[otherIndex + 2];
            var alpha = m_Colors[index + 3] - other.m_Colors[otherIndex + 3];
            return Math.Sqrt(red * red + green * green + blue * blue + alpha * alpha) / 2.0;
        }

        internal double StructureDistance(int x, int y, FeatureMap other, int otherX, int otherY)
        {
            var index = y * Width + x;
            var otherIndex = otherY * other.Width + otherX;
            var horizontal = m_HorizontalEdges[index] - other.m_HorizontalEdges[otherIndex];
            var vertical = m_VerticalEdges[index] - other.m_VerticalEdges[otherIndex];
            return Math.Sqrt(horizontal * horizontal + vertical * vertical) / Math.Sqrt(2.0);
        }
    }

    private struct RegionAccumulator
    {
        internal double ColorMismatch;
        internal double StructureMismatch;
        internal int SampleCount;
    }

    private sealed record RegionalScores(double ColorMismatch, double StructureMismatch,
        double WorstRegionMismatch, IReadOnlyList<RegionReport> WorstRegions);
}

internal sealed record RegionReport(int X, int Y, int Width, int Height, double ColorMismatch,
    double StructureMismatch, double CombinedMismatch)
{
    internal double ColorSimilarityPercent => 100.0 * (1.0 - ColorMismatch);
    internal double StructureSimilarityPercent => 100.0 * (1.0 - StructureMismatch);
}

internal sealed record PerceptualComparison(bool IsEquivalent, int ExpectedWidth, int ExpectedHeight,
    int ActualWidth, int ActualHeight, double DctSimilarityPercent, double DctThresholdPercent,
    double GlobalColorSimilarityPercent, double GlobalStructureSimilarityPercent,
    double LocalSimilarityPercent, IReadOnlyList<RegionReport> WorstRegions)
{
    internal bool DimensionsMatch => ExpectedWidth == ActualWidth && ExpectedHeight == ActualHeight;

    internal static PerceptualComparison DimensionMismatch(int expectedWidth, int expectedHeight,
        int actualWidth, int actualHeight, double dctThresholdPercent)
        => new(false, expectedWidth, expectedHeight, actualWidth, actualHeight, 0.0,
            dctThresholdPercent, 0.0, 0.0, 0.0, []);
}
