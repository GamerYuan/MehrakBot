using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace Mehrak.ImageProcessor.Shared.Services;

/// <summary>
/// Determines whether a reference image (e.g. a character's square face crop from the
/// game record API) is a cropped region of a candidate image (e.g. a full character
/// portrait from the wiki gallery). Uses AKAZE feature matching with a Lowe ratio test
/// followed by a RANSAC affine fit; the inlier ratio decides the match.
/// </summary>
public class PortraitImageMatcher
{
    private const double RatioThreshold = 0.75;
    private const int MinKeypoints = 6;
    private const int MinInliers = 8;
    private const double MinInlierRatio = 0.4;
    private const double RansacReprojThreshold = 5.0;

    public virtual (bool IsMatch, float Confidence) Match(byte[] reference, byte[] candidate)
    {
        using var referenceMat = Cv2.ImDecode(reference, ImreadModes.Grayscale);
        using var candidateMat = Cv2.ImDecode(candidate, ImreadModes.Grayscale);

        if (referenceMat.Empty() || candidateMat.Empty())
            return (false, 0f);

        NormalizeToMax1024(referenceMat);
        NormalizeToMax1024(candidateMat);

        using var akaze = AKAZE.Create();
        using var referenceDesc = new Mat();
        using var candidateDesc = new Mat();
        akaze.DetectAndCompute(referenceMat, null, out var referenceKps, referenceDesc);
        akaze.DetectAndCompute(candidateMat, null, out var candidateKps, candidateDesc);

        if (referenceDesc.Empty() || candidateDesc.Empty()
            || referenceKps.Length < MinKeypoints || candidateKps.Length < MinKeypoints)
            return (false, 0f);

        using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);
        var knnMatches = matcher.KnnMatch(referenceDesc, candidateDesc, k: 2);

        var goodMatches = new List<DMatch>();
        foreach (var matchSet in knnMatches)
        {
            if (matchSet.Length >= 2 && matchSet[0].Distance < RatioThreshold * matchSet[1].Distance)
            {
                goodMatches.Add(matchSet[0]);
            }
        }

        if (goodMatches.Count < MinInliers)
            return (false, 0f);

        var srcPoints = goodMatches.Select(m => referenceKps[m.QueryIdx].Pt).ToArray();
        var dstPoints = goodMatches.Select(m => candidateKps[m.TrainIdx].Pt).ToArray();

        using var inlierMask = new Mat();
        using var affine = Cv2.EstimateAffinePartial2D(
            InputArray.Create(srcPoints), InputArray.Create(dstPoints), inlierMask,
            RobustEstimationAlgorithms.RANSAC, RansacReprojThreshold);

        if (affine is null || affine.Empty())
            return (false, 0f);

        // Validate determinant (no flip, no near-singular transform)
        var det = affine.At<double>(0, 0) * affine.At<double>(1, 1)
                - affine.At<double>(0, 1) * affine.At<double>(1, 0);

        if (det <= 0 || det <= 0.01)
            return (false, 0f);

        var inliers = Cv2.CountNonZero(inlierMask);
        var ratio = inliers / (double)goodMatches.Count;

        return (inliers >= MinInliers && ratio >= MinInlierRatio, (float)ratio);
    }

    private static void NormalizeToMax1024(Mat src)
    {
        var maxDim = Math.Max(src.Width, src.Height);
        if (maxDim <= 1024)
            return;

        var scaleFactor = 1024.0 / maxDim;
        using var resized = new Mat();
        Cv2.Resize(src, resized,
            new Size((int)(src.Width * scaleFactor), (int)(src.Height * scaleFactor)),
            interpolation: InterpolationFlags.Cubic);
        resized.CopyTo(src);
    }
}
