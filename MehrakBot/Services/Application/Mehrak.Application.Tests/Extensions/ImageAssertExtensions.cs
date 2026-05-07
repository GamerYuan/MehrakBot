using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using NUnit.Framework.Constraints;
using SixLabors.ImageSharp;

namespace Mehrak.Application.Tests.Extensions;

public static class IsImage
{
    private static readonly AverageHash HashAlgorithm = new();

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
    private readonly byte[] _expected;
    private readonly double _similarityThreshold;
    private double _actualSimilarity;
    private static readonly AverageHash HashAlgorithm = new();

    public ImageIdenticalConstraint(byte[] expected, double similarityThreshold)
    {
        _expected = expected;
        _similarityThreshold = similarityThreshold;
        _actualSimilarity = -1;
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
        using var expectedMs = new MemoryStream(_expected);

        var actualHash = HashAlgorithm.Hash(actualMs);
        var expectedHash = HashAlgorithm.Hash(expectedMs);

        _actualSimilarity = CompareHash.Similarity(actualHash, expectedHash);

        var isSuccess = _actualSimilarity >= _similarityThreshold;

        return new ImageConstraintResult(this, actual, isSuccess, _actualSimilarity, _similarityThreshold);
    }

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public override string Description
        => $"image with >= {_similarityThreshold}% perceptual similarity to golden image";

    private class ImageConstraintResult : ConstraintResult
    {
        private readonly double _similarity;
        private readonly double _threshold;

        public ImageConstraintResult(IConstraint constraint, object? actualValue, bool isSuccess,
            double similarity, double threshold)
            : base(constraint, actualValue, isSuccess)
        {
            _similarity = similarity;
            _threshold = threshold;
        }

        public override void WriteMessageTo(MessageWriter writer)
        {
            writer.Write($"Expected image to have >= {_threshold}% perceptual similarity to golden image, " +
                         $"but similarity was {_similarity:F2}%.");
        }
    }
}
