using Mehrak.Domain.Models.Abstractions;
using OpenCvSharp;

namespace Mehrak.Application.Services.Genshin.Types;

internal class GenshinWeaponImageProcessor : IMultiImageProcessor
{
    public bool ShouldProcess => true;

    public Stream ProcessImage(IEnumerable<Stream> images)
    {
        var imageList = images.Select(StreamToMat).ToList();

        var icon = imageList[0];
        var ascended = imageList[2];
        var original = new Mat();

        if (imageList[1].Width >= 800 || imageList[1].Height >= 800)
        {
            using var largeOriginal = imageList[1];
            var scaleFactor = 0f;
            if (largeOriginal.Width > largeOriginal.Height)
            {
                scaleFactor = 800f / largeOriginal.Width;
            }
            else
            {
                scaleFactor = 800f / largeOriginal.Height;
            }
            Cv2.Resize(largeOriginal, original, new Size((int)(largeOriginal.Width * scaleFactor), (int)(largeOriginal.Height * scaleFactor)));
        }
        else
        {
            original.Dispose();
            original = imageList[1];
        }

        var iconAngle = GetOrientationPCA(icon);
        var originalAngle = GetOrientationPCA(original);

        var rotation = originalAngle - iconAngle;

        using var rotatedOriginal = RotateImage(original, rotation);

        var cropRect = FindIconWithScaling(rotatedOriginal, icon);

        if (cropRect is null)
        {
            return Stream.Null;
        }

        using var rotatedAscended = RotateImage(ascended, rotation);
        using var scaledAscended = new Mat();
        Cv2.Resize(rotatedAscended, scaledAscended, new Size(rotatedOriginal.Width, rotatedOriginal.Height));

        var finalRect = Rect.Intersect(cropRect.Value, new Rect(0, 0, scaledAscended.Width, scaledAscended.Height));

        using var crop = new Mat(scaledAscended, finalRect);
        using var output = new Mat();
        Cv2.Resize(crop, output, new Size(200, 200));

        imageList.ForEach(x => x.Dispose());

        return new MemoryStream(output.ImEncode(".png"));
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

    private static double GetOrientationPCA(Mat img)
    {
        List<Point2f> dataPoints = [];

        var indexer = img.GetGenericIndexer<Vec4b>();
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++)
            {
                // Alpha threshold > 10
                if (indexer[y, x].Item3 > 10) dataPoints.Add(new Point2f(x, y));
            }
        }

        if (dataPoints.Count == 0) return 0;

        using var dataMat = new Mat(dataPoints.Count, 2, MatType.CV_32F);
        for (var i = 0; i < dataPoints.Count; i++)
        {
            dataMat.Set(i, 0, dataPoints[i].X);
            dataMat.Set(i, 1, dataPoints[i].Y);
        }

        using var pca = new PCA(dataMat, new Mat(), PCA.Flags.DataAsRow);

        // Get primary eigenvector components
        var vx = pca.Eigenvectors.At<float>(0, 0);
        var vy = pca.Eigenvectors.At<float>(0, 1);

        // Calculate Angle
        var angleRad = Math.Atan2(vy, vx);
        var angleDeg = angleRad * (180.0 / Math.PI);

        if (angleDeg > 90)
        {
            angleDeg -= 180;
        }
        else if (angleDeg < -90)
        {
            angleDeg += 180;
        }

        return angleDeg;
    }

    private static Rect? FindIconWithScaling(Mat searchImage, Mat templateIcon)
    {
        double bestScore = -1;
        var bestRect = new Rect();

        using var searchAlpha = searchImage.ExtractChannel(3);
        using var templateAlpha = templateIcon.ExtractChannel(3);

        var minScale = 0.2;
        var maxScale = 2.0;
        var step = 0.05;

        for (var scale = minScale; scale <= maxScale; scale += step)
        {
            var newSize = new Size((int)(templateAlpha.Width * scale), (int)(templateAlpha.Height * scale));

            if (newSize.Width > searchAlpha.Width || newSize.Height > searchAlpha.Height) continue;

            // Resize ONLY the Alpha channel of the template
            using var scaledTemplateAlpha = new Mat();
            Cv2.Resize(templateAlpha, scaledTemplateAlpha, newSize, 0, 0, InterpolationFlags.Area);

            // Match using the Alpha maps
            using var result = new Mat();
            Cv2.MatchTemplate(searchAlpha, scaledTemplateAlpha, result, TemplateMatchModes.CCoeffNormed);

            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal > bestScore)
            {
                bestScore = maxVal;
                bestRect = new Rect(maxLoc, newSize);
            }
        }

        if (bestScore < 0.5)
            return null;

        return bestRect;
    }

    private static Mat RotateImage(Mat src, double angle)
    {
        var center = new Point2f(src.Width / 2f, src.Height / 2f);
        var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);

        var bbox = new RotatedRect(center, src.Size(), (float)angle).BoundingRect();

        // Adjust the rotation matrix to center the image in the new bounds
        rotMat.Set(0, 2, rotMat.At<double>(0, 2) + bbox.Width / 2.0 - center.X);
        rotMat.Set(1, 2, rotMat.At<double>(1, 2) + bbox.Height / 2.0 - center.Y);

        var dst = new Mat();
        Cv2.WarpAffine(src, dst, rotMat, new Size(bbox.Width, bbox.Height),
            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));

        return dst;
    }
}
