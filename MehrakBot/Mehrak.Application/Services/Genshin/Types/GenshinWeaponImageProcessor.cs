using Mehrak.Domain.Models.Abstractions;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.Flann;

namespace Mehrak.Application.Services.Genshin.Types;

internal class GenshinWeaponImageProcessor : IMultiImageProcessor
{
    public bool ShouldProcess => true;

    public Stream ProcessImage(IEnumerable<Stream> images)
    {
        var imageList = images.Select(StreamToMat).ToList();

        using var icon = imageList[0];
        using var ascended = new Mat();

        if (imageList[1].Size().Width > 800 || imageList[1].Size().Height > 800)
        {
            var scaleFactor = Math.Max(
                (double)800 / imageList[1].Width,
                (double)800 / imageList[1].Height
            );
            Cv2.Resize(imageList[1], ascended,
                new Size(imageList[1].Size().Width * scaleFactor, imageList[1].Size().Height * scaleFactor),
                interpolation: InterpolationFlags.Cubic);
        }
        else
        {
            imageList[1].CopyTo(ascended);
        }

        imageList[1].Dispose();

        if (icon.Empty() || ascended.Empty())
            return Stream.Null;

        using var iconAlpha = icon.ExtractChannel(3);
        using var ascendedAlpha = ascended.ExtractChannel(3);

        var homography = FindHomography(ascendedAlpha, iconAlpha);

        // Validate homography from alpha; if invalid, try grayscale matching
        if (!IsHomographyValid(homography, ascended.Size(), icon.Size()))
        {
            using var ascendedGray = new Mat();
            using var iconGray = new Mat();

            Cv2.CvtColor(ascended, ascendedGray, ColorConversionCodes.BGRA2GRAY);
            Cv2.CvtColor(icon, iconGray, ColorConversionCodes.BGRA2GRAY);

            homography = FindHomography(ascendedGray, iconGray);

            if (!IsHomographyValid(homography, ascended.Size(), icon.Size()))
                return Stream.Null;
        }

        // 2. Warp the Ascended Image
        // This applies Rotation, Scale, and Translation in one step.
        // The result will be the Ascended image transformed so that the target object
        // aligns perfectly with the Icon's dimensions and position.
        using var warpedAscended = new Mat();

        // WarpPerspective automatically handles the geometric transformation.
        // We set the destination size to the Icon's size, effectively cropping it simultaneously.
        Cv2.WarpPerspective(ascended, warpedAscended, homography, new Size(icon.Width, icon.Height));

        using var final = new Mat();
        Cv2.Resize(warpedAscended, final, new Size(200, 200));

        homography.Dispose();

        // 3. Encode and Return
        return new MemoryStream(final.ImEncode(".png"));
    }

    private static Mat FindHomography(Mat src, Mat dst)
    {
        // 1. SIFT Feature Detector
        // nFeatures: 0 means find as many as possible.
        // nOctaveLayers: 3 is standard.
        // contrastThreshold: 0.04 is standard.
        // edgeThreshold: 10 is standard.
        // sigma: 1.6 is standard.
        using var sift = SIFT.Create();

        using var descriptorsSrc = new Mat();
        using var descriptorsDst = new Mat();

        // Detect and Compute
        sift.DetectAndCompute(src, null, out var keypointsSrc, descriptorsSrc);
        sift.DetectAndCompute(dst, null, out var keypointsDst, descriptorsDst);

        if (keypointsSrc.Length < 4 || keypointsDst.Length < 4)
            return new Mat();

        // 2. FLANN Matcher
        // FLANN parameters for SIFT (KD-Tree algorithm)
        var indexParams = new KDTreeIndexParams(trees: 5);
        var searchParams = new SearchParams(checks: 50);

        using var matcher = new FlannBasedMatcher(indexParams, searchParams);

        // KNN Match (k=2) is standard for SIFT to apply Lowe's Ratio Test
        var knnMatches = matcher.KnnMatch(descriptorsSrc, descriptorsDst, k: 2);

        // 3. Filter Matches (Lowe's Ratio Test)
        // This filters out ambiguous matches where the best match is not significantly better than the second best.
        List<DMatch> goodMatches = [];
        var ratioThresh = 0.75f;

        foreach (var matchSet in knnMatches)
        {
            if (matchSet.Length >= 2 && matchSet[0].Distance < ratioThresh * matchSet[1].Distance)
            {
                goodMatches.Add(matchSet[0]);
            }
        }

        if (goodMatches.Count < 4)
            return new Mat();

        // 4. Extract Points for Homography
        List<Point2f> srcPoints = [];
        List<Point2f> dstPoints = [];

        foreach (var m in goodMatches)
        {
            srcPoints.Add(keypointsSrc[m.QueryIdx].Pt);
            dstPoints.Add(keypointsDst[m.TrainIdx].Pt);
        }

        // 5. Find Homography with RANSAC
        return Cv2.FindHomography(
            InputArray.Create(srcPoints),
            InputArray.Create(dstPoints),
            HomographyMethods.Ransac,
            ransacReprojThreshold: 5.0
        );
    }

    private static bool IsHomographyValid(Mat h, Size srcSize, Size dstSize)
    {
        if (h.Empty()) return false;

        // 1. Check Determinant of the top-left 2x2 matrix (Rotation/Scale)
        // This detects if the image is flipping (negative det) or collapsing (near zero).
        var det = h.At<double>(0, 0) * h.At<double>(1, 1) - h.At<double>(1, 0) * h.At<double>(0, 1);

        if (det < 0) return false; // Image is flipped (mirror), likely invalid for icons
        if (Math.Abs(det) < 0.0001) return false; // Matrix is singular/collapsing

        // 2. Check Geometric Distortion
        // Transform the 4 corners of the source image to see where they land
        var srcCorners = new[]
        {
            new Point2f(0, 0),
            new Point2f(srcSize.Width, 0),
            new Point2f(srcSize.Width, srcSize.Height),
            new Point2f(0, srcSize.Height)
        };

        var dstCorners = Cv2.PerspectiveTransform(srcCorners, h);

        // A. Convexity Check
        // If the transformed shape is "twisted" (bowtie shape), IsContourConvex returns false
        if (!Cv2.IsContourConvex(dstCorners)) return false;

        // B. Scale/Area Check
        // Calculate the area of the transformed corners
        var area = Cv2.ContourArea(dstCorners);

        // Define limits relative to the destination icon size
        // e.g., The warped image shouldn't be smaller than 1% of the icon
        // or larger than 100x the icon.
        double iconArea = dstSize.Width * dstSize.Height;

        if (area < iconArea * 0.01) return false; // Too small (imploded)
        if (area > iconArea * 100.0) return false; // Too big (exploded to infinity)

        return true;
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
