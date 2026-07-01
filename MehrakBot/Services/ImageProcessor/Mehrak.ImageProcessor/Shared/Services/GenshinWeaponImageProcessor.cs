using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace Mehrak.ImageProcessor.Shared.Services;

public class GenshinWeaponImageProcessor
{
    private const double RatioThreshold = 0.75;
    private const int MinKeypoints = 4;
    private const int MinGoodMatches = 4;
    private const double RansacReprojThreshold = 5.0;
    private const double IouAcceptThreshold = 0.80;

    public virtual Stream ProcessImage(IEnumerable<Stream> images)
    {
        var imageList = images.Take(2).Select(StreamToMat).ToList();

        if (imageList.Count < 2)
        {
            foreach (var mat in imageList) mat.Dispose();
            throw new ArgumentException("At least two images are required: Icon and Ascended Image", nameof(images));
        }

        try
        {
            using var icon = imageList[0];
            using var ascended = new Mat();

            // Normalize both images to max 512px on longest side
            NormalizeToMax512(icon);
            NormalizeToMax512(imageList[1]);

            Cv2.CopyTo(imageList[1], ascended);
            imageList[1].Dispose();

            if (icon.Empty() || ascended.Empty())
                return Stream.Null;

            // Guard: ExtractChannel(3) requires 4-channel BGRA input
            if (icon.Channels() < 4 || ascended.Channels() < 4)
                return Stream.Null;

            // Extract alpha channels
            using var iconAlpha = icon.ExtractChannel(3);
            using var ascendedAlpha = ascended.ExtractChannel(3);

            // AKAZE feature detection on alpha images
            using var akaze = AKAZE.Create();
            using var ascendedDesc = new Mat();
            using var iconDesc = new Mat();

            akaze.DetectAndCompute(ascendedAlpha, null, out var ascendedKps, ascendedDesc);
            akaze.DetectAndCompute(iconAlpha, null, out var iconKps, iconDesc);

            if (ascendedDesc.Empty() || iconDesc.Empty()
                || ascendedKps.Length < MinKeypoints || iconKps.Length < MinKeypoints)
                return Stream.Null;

            // BFMatcher with Hamming norm for binary descriptors
            using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);
            var knnMatches = matcher.KnnMatch(ascendedDesc, iconDesc, k: 2);

            // Lowe's ratio test
            var goodMatches = new List<DMatch>();
            foreach (var matchSet in knnMatches)
            {
                if (matchSet.Length >= 2 && matchSet[0].Distance < RatioThreshold * matchSet[1].Distance)
                {
                    goodMatches.Add(matchSet[0]);
                }
            }

            if (goodMatches.Count < MinGoodMatches)
                return Stream.Null;

            // Extract matched point pairs
            var srcPoints = goodMatches.Select(m => ascendedKps[m.QueryIdx].Pt).ToArray();
            var dstPoints = goodMatches.Select(m => iconKps[m.TrainIdx].Pt).ToArray();

            // Estimate affine transform
            using var affineMat = Cv2.EstimateAffinePartial2D(
                InputArray.Create(srcPoints), InputArray.Create(dstPoints), null,
                RobustEstimationAlgorithms.RANSAC,
                ransacReprojThreshold: RansacReprojThreshold);

            if (affineMat is null || affineMat.Empty())
                return Stream.Null;

            // Validate determinant (no flip, no near-singular)
            var det = affineMat.At<double>(0, 0) * affineMat.At<double>(1, 1)
                    - affineMat.At<double>(0, 1) * affineMat.At<double>(1, 0);

            if (det <= 0 || det <= 0.01)
                return Stream.Null;

            // Warp ascended image
            using var warpedAscended = new Mat();
            Cv2.WarpAffine(ascended, warpedAscended, affineMat,
                new Size(icon.Width, icon.Height),
                borderValue: new Scalar(0, 0, 0, 0));

            // IoU after initial affine warp
            using var warpedAlpha = new Mat();
            Cv2.WarpAffine(ascendedAlpha, warpedAlpha, affineMat,
                new Size(icon.Width, icon.Height));

            var iou = ComputeIoU(warpedAlpha, iconAlpha);

            if (iou < IouAcceptThreshold)
            {
                // ECC refinement on the original ascended (not the already-warped image)
                iou = AttemptEccRefinement(ascended, ascendedAlpha, icon, iconAlpha, affineMat);

                if (iou < IouAcceptThreshold)
                    return Stream.Null;

                // Re-warp with refined transform for final output
                Cv2.WarpAffine(ascended, warpedAscended, affineMat,
                    new Size(icon.Width, icon.Height),
                    borderValue: new Scalar(0, 0, 0, 0));
            }

            // Final resize and encode
            using var final = new Mat();
            Cv2.Resize(warpedAscended, final, new Size(200, 200), interpolation: InterpolationFlags.Cubic);

            return new MemoryStream(final.ImEncode(".png"));
        }
        finally
        {
            foreach (var mat in imageList)
            {
                if (!mat.IsDisposed)
                    mat.Dispose();
            }
        }
    }

    private static void NormalizeToMax512(Mat src)
    {
        var maxDim = Math.Max(src.Width, src.Height);
        if (maxDim <= 512)
            return;

        var scaleFactor = 512.0 / maxDim;
        using var resized = new Mat();
        Cv2.Resize(src, resized,
            new Size((int)(src.Width * scaleFactor), (int)(src.Height * scaleFactor)),
            interpolation: InterpolationFlags.Cubic);
        resized.CopyTo(src);
    }

    private static double ComputeIoU(Mat warpedAlpha, Mat iconAlpha)
    {
        using var binaryWarped = new Mat();
        using var binaryIcon = new Mat();
        using var intersection = new Mat();
        using var union = new Mat();

        Cv2.Threshold(warpedAlpha, binaryWarped, 128, 255, ThresholdTypes.Binary);
        Cv2.Threshold(iconAlpha, binaryIcon, 128, 255, ThresholdTypes.Binary);

        Cv2.BitwiseAnd(binaryWarped, binaryIcon, intersection);
        Cv2.BitwiseOr(binaryWarped, binaryIcon, union);

        var interCount = Cv2.CountNonZero(intersection);
        var unionCount = Cv2.CountNonZero(union);

        return unionCount == 0 ? 0 : interCount / (double)unionCount;
    }

    private static double AttemptEccRefinement(
        Mat ascended, Mat ascendedAlpha,
        Mat icon, Mat iconAlpha,
        Mat affineMat)
    {
        using var ascendedGray = new Mat();
        using var iconGray = new Mat();

        Cv2.CvtColor(ascended, ascendedGray, ColorConversionCodes.BGRA2GRAY);
        Cv2.CvtColor(icon, iconGray, ColorConversionCodes.BGRA2GRAY);

        // Warp ascended grayscale using current affine, then let ECC find residual
        using var warpedGray = new Mat();
        Cv2.WarpAffine(ascendedGray, warpedGray, affineMat,
            new Size(icon.Width, icon.Height));

        // Initialize residual as identity (Euclidean: translation + rotation + uniform scale)
        using var warpMatrix = new Mat(2, 3, MatType.CV_64F);
        warpMatrix.Set(0, 0, 1.0);
        warpMatrix.Set(0, 1, 0.0);
        warpMatrix.Set(0, 2, 0.0);
        warpMatrix.Set(1, 0, 0.0);
        warpMatrix.Set(1, 1, 1.0);
        warpMatrix.Set(1, 2, 0.0);

        try
        {
            Cv2.FindTransformECC(
                iconGray, warpedGray, warpMatrix,
                MotionTypes.Euclidean,
                new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 10, 1e-4));

            // Compose: refined = residual × initial
            ComposeAffine(warpMatrix, affineMat, affineMat);

            // Re-warp alpha and recompute IoU
            using var refinedAlpha = new Mat();
            Cv2.WarpAffine(ascendedAlpha, refinedAlpha, affineMat,
                new Size(icon.Width, icon.Height));

            return ComputeIoU(refinedAlpha, iconAlpha);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Composes two 2x3 affine transforms: dst = residual × src.
    /// residual is 2x3 (Euclidean), src is 2x3, result stored in dst (can alias src).
    /// </summary>
    private static void ComposeAffine(Mat residual, Mat src, Mat dst)
    {
        // Extract residual components
        var rCos = residual.At<double>(0, 0);
        var rSin = residual.At<double>(1, 0);
        var rTx = residual.At<double>(0, 2);
        var rTy = residual.At<double>(1, 2);

        // Extract source components
        var sCos = src.At<double>(0, 0);
        var sSin = src.At<double>(1, 0);
        var sTx = src.At<double>(0, 2);
        var sTy = src.At<double>(1, 2);

        // Compose: new rotation = residual * source
        var cos = rCos * sCos - rSin * sSin;
        var sin = rSin * sCos + rCos * sSin;

        // Compose: new translation = residual.rotate(source.translation) + residual.translation
        var tx = rCos * sTx - rSin * sTy + rTx;
        var ty = rSin * sTx + rCos * sTy + rTy;

        dst.Set(0, 0, cos);
        dst.Set(0, 1, -sin);
        dst.Set(0, 2, tx);
        dst.Set(1, 0, sin);
        dst.Set(1, 1, cos);
        dst.Set(1, 2, ty);
    }

    private static Mat StreamToMat(Stream stream)
    {
        if (stream is MemoryStream ms)
        {
            return Cv2.ImDecode(ms.ToArray(), ImreadModes.Unchanged);
        }
        else
        {
            using var temp = new MemoryStream();
            stream.CopyTo(temp);
            return Cv2.ImDecode(temp.ToArray(), ImreadModes.Unchanged);
        }
    }
}
