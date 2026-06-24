// AverageHash ported from Coenm.ImageHash (MIT) — see https://github.com/coenm/ImageHash
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using NUnit.Framework.Constraints;

namespace Mehrak.Application.Tests.TestUtils;

public static class IsImage
{
    public static ImageIdenticalConstraint IdenticalTo(byte[] expected, double similarityThreshold = 98.0)
        => new(expected, similarityThreshold);

    public static ImageIdenticalConstraint IdenticalTo(Stream expected, double similarityThreshold = 98.0)
    {
        using var ms = new MemoryStream();
        expected.Position = 0;
        expected.CopyTo(ms);
        return new(ms.ToArray(), similarityThreshold);
    }
}

public class ImageIdenticalConstraint : Constraint
{
    private readonly byte[] m_Expected;
    private readonly double m_SimilarityThreshold;
    public ImageIdenticalConstraint(byte[] expected, double similarityThreshold)
    {
        m_Expected = expected;
        m_SimilarityThreshold = similarityThreshold;
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        var actualBytes = actual switch
        {
            byte[] bytes => bytes,
            Stream stream => ReadStream(stream),
            _ => throw new ArgumentException($"Expected byte[] or Stream, got {typeof(TActual)}")
        };

        using var actualMs = new MemoryStream(actualBytes);
        using var expectedMs = new MemoryStream(m_Expected);

        var actualHash = AverageHash(actualMs);
        var expectedHash = AverageHash(expectedMs);

        var actualSimilarity = Similarity(actualHash, expectedHash);

        var isSuccess = actualSimilarity >= m_SimilarityThreshold;

        return new ImageConstraintResult(this, actual, isSuccess, actualSimilarity, m_SimilarityThreshold);
    }

    private static ulong AverageHash(Stream stream)
    {
        using var image = Image.Load<Rgba32>(stream);
        image.Mutate(ctx => ctx
            .Resize(8, 8)
            .Grayscale(GrayscaleMode.Bt601)
            .AutoOrient());

        ulong hash = 0;

        image.ProcessPixelRows(accessor =>
        {
            uint average = 0;
            for (var y = 0; y < 8; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 8; x++)
                    average += row[x].R;
            }
            average /= 64;

            var mask = 1UL << 63;
            for (var y = 0; y < 8; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 8; x++)
                {
                    if (row[x].R >= average)
                        hash |= mask;
                    mask >>= 1;
                }
            }
        });

        return hash;
    }

    private static double Similarity(ulong a, ulong b)
        => (64 - BitOperations.PopCount(a ^ b)) / 64.0 * 100.0;

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public override string Description
        => $"image with >= {m_SimilarityThreshold}% perceptual similarity to golden image";

    private class ImageConstraintResult : ConstraintResult
    {
        private readonly double m_Similarity;
        private readonly double m_Threshold;

        public ImageConstraintResult(IConstraint constraint, object? actualValue, bool isSuccess,
            double similarity, double threshold)
            : base(constraint, actualValue, isSuccess)
        {
            m_Similarity = similarity;
            m_Threshold = threshold;
        }

        public override void WriteMessageTo(MessageWriter writer)
        {
            writer.Write($"Expected image to have >= {m_Threshold}% perceptual similarity to golden image, " +
                         $"but similarity was {m_Similarity:F2}%.");
        }
    }
}
