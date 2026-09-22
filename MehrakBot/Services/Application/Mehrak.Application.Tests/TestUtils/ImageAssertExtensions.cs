using NUnit.Framework.Constraints;

namespace Mehrak.Application.Tests.TestUtils;

public static class IsImage
{
    /// <summary>
    /// Retained for existing golden-image tests. This is a perceptual comparison, not byte or pixel identity.
    /// The optional threshold is the minimum global DCT-hash similarity percentage; fixed color, structure,
    /// dimension, and localized-region guardrails are also required.
    /// </summary>
    public static ImageIdenticalConstraint IdenticalTo(byte[] expected,
        double similarityThreshold = PerceptualImageComparator.DefaultDctSimilarityPercent)
        => PerceptuallyEquivalentTo(expected, similarityThreshold);

    /// <inheritdoc cref="IdenticalTo(byte[],double)"/>
    public static ImageIdenticalConstraint IdenticalTo(Stream expected,
        double similarityThreshold = PerceptualImageComparator.DefaultDctSimilarityPercent)
        => PerceptuallyEquivalentTo(expected, similarityThreshold);

    public static ImageIdenticalConstraint PerceptuallyEquivalentTo(byte[] expected,
        double minimumDctSimilarityPercent = PerceptualImageComparator.DefaultDctSimilarityPercent)
        => new(expected, minimumDctSimilarityPercent);

    public static ImageIdenticalConstraint PerceptuallyEquivalentTo(Stream expected,
        double minimumDctSimilarityPercent = PerceptualImageComparator.DefaultDctSimilarityPercent)
        => new(ReadStreamPreservingPosition(expected), minimumDctSimilarityPercent);

    private static byte[] ReadStreamPreservingPosition(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var originalPosition = stream.CanSeek ? stream.Position : (long?)null;
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            if (originalPosition.HasValue)
                stream.Position = originalPosition.Value;
        }
    }
}

public sealed class ImageIdenticalConstraint : Constraint
{
    private readonly byte[] m_Expected;
    private readonly double m_MinimumDctSimilarityPercent;

    public ImageIdenticalConstraint(byte[] expected, double minimumDctSimilarityPercent)
    {
        ArgumentNullException.ThrowIfNull(expected);
        PerceptualImageComparator.ValidateThreshold(minimumDctSimilarityPercent);
        m_Expected = (byte[])expected.Clone();
        m_MinimumDctSimilarityPercent = minimumDctSimilarityPercent;
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        var actualBytes = actual switch
        {
            byte[] bytes => bytes,
            Stream stream => ReadStreamPreservingPosition(stream),
            _ => throw new ArgumentException($"Expected byte[] or Stream, got {typeof(TActual)}")
        };

        var comparison = PerceptualImageComparator.Compare(m_Expected, actualBytes,
            m_MinimumDctSimilarityPercent);
        return new ImageConstraintResult(this, actual, comparison);
    }

    private static byte[] ReadStreamPreservingPosition(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : (long?)null;
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            if (originalPosition.HasValue)
                stream.Position = originalPosition.Value;
        }
    }

    public override string Description
        => $"same-size image with >= {m_MinimumDctSimilarityPercent:F2}% global DCT-hash similarity "
           + "and the calibrated color, structure, and localized-detail guardrails";

    private sealed class ImageConstraintResult : ConstraintResult
    {
        private readonly PerceptualComparison m_Comparison;

        internal ImageConstraintResult(IConstraint constraint, object? actualValue,
            PerceptualComparison comparison)
            : base(constraint, actualValue, comparison.IsEquivalent)
        {
            m_Comparison = comparison;
        }

        public override void WriteMessageTo(MessageWriter writer)
        {
            if (!m_Comparison.DimensionsMatch)
            {
                writer.Write($"Expected oriented dimensions {m_Comparison.ExpectedWidth}x{m_Comparison.ExpectedHeight}, "
                             + $"but actual oriented dimensions were {m_Comparison.ActualWidth}x{m_Comparison.ActualHeight}.");
                return;
            }

            writer.Write("Perceptual image comparison failed. "
                         + $"DCT hash: {m_Comparison.DctSimilarityPercent:F2}% "
                         + $"(minimum {m_Comparison.DctThresholdPercent:F2}%); "
                         + $"global color: {m_Comparison.GlobalColorSimilarityPercent:F2}% "
                         + $"(minimum {PerceptualImageComparator.MinimumGlobalColorSimilarityPercent:F2}%); "
                         + $"global structure: {m_Comparison.GlobalStructureSimilarityPercent:F2}% "
                         + $"(minimum {PerceptualImageComparator.MinimumGlobalStructureSimilarityPercent:F2}%); "
                         + $"local detail: {m_Comparison.LocalSimilarityPercent:F2}% "
                         + $"(minimum {PerceptualImageComparator.MinimumLocalSimilarityPercent:F2}%).");

            if (m_Comparison.WorstRegions.Count == 0)
                return;

            writer.Write(" Most different regions (expected-image coordinates): ");
            for (var index = 0; index < m_Comparison.WorstRegions.Count; index++)
            {
                if (index > 0)
                    writer.Write("; ");

                var region = m_Comparison.WorstRegions[index];
                writer.Write($"[{region.X},{region.Y},{region.Width}x{region.Height}: "
                             + $"color {region.ColorSimilarityPercent:F2}%, "
                             + $"structure {region.StructureSimilarityPercent:F2}%]");
            }
        }
    }
}
