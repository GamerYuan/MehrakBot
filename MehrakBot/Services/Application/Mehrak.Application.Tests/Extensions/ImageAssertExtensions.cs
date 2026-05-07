using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using NUnit.Framework.Constraints;

namespace Mehrak.Application.Tests.Extensions;

public static class IsImage
{
    public static ImageIdenticalConstraint IdenticalTo(byte[] expected, double similarityThreshold = 98.0)
        => new(expected, similarityThreshold);

    public static ImageIdenticalConstraint IdenticalTo(Stream expected, double similarityThreshold = 98.0)
    {
        using var ms = new MemoryStream();
        expected.CopyTo(ms);
        return new(ms.ToArray(), similarityThreshold);
    }
}

public class ImageIdenticalConstraint : Constraint
{
    private readonly byte[] m_Expected;
    private readonly double m_SimilarityThreshold;
    private double m_ActualSimilarity;
    public ImageIdenticalConstraint(byte[] expected, double similarityThreshold)
    {
        m_Expected = expected;
        m_SimilarityThreshold = similarityThreshold;
        m_ActualSimilarity = -1;
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        byte[] actualBytes = actual switch
        {
            byte[] bytes => bytes,
            Stream stream => ReadStream(stream),
            _ => throw new ArgumentException($"Expected byte[] or Stream, got {typeof(TActual)}")
        };

        using var actualMs = new MemoryStream(actualBytes);
        using var expectedMs = new MemoryStream(m_Expected);

        var hashAlgorithm = new AverageHash();
        var actualHash = hashAlgorithm.Hash(actualMs);
        var expectedHash = hashAlgorithm.Hash(expectedMs);

        m_ActualSimilarity = CompareHash.Similarity(actualHash, expectedHash);

        var isSuccess = m_ActualSimilarity >= m_SimilarityThreshold;

        return new ImageConstraintResult(this, actual, isSuccess, m_ActualSimilarity, m_SimilarityThreshold);
    }

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
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
