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
        using var ascended = imageList[1];

        if (icon.Empty() || ascended.Empty())
            return Stream.Null;

        // 1. Feature Detection & Matching
        // We need to find the transformation (Matrix) that maps 'Original' -> 'Icon'.
        // Once we have that, we apply it to 'Ascended' (which is 1:1 with Original).
        var homography = FindHomography(ascended, icon);

        if (homography.Empty())
            return Stream.Null;

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

        // Extract alpha for detection
        using var srcGray = src.ExtractChannel(3);
        using var dstGray = dst.ExtractChannel(3);

        using var descriptorsSrc = new Mat();
        using var descriptorsDst = new Mat();

        // Detect and Compute
        sift.DetectAndCompute(srcGray, null, out var keypointsSrc, descriptorsSrc);
        sift.DetectAndCompute(dstGray, null, out var keypointsDst, descriptorsDst);

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
