using Mehrak.Application.Services.Genshin.Types;
using OpenCvSharp;

namespace Mehrak.Application.Tests.Services.Genshin.Types;

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class GenshinWeaponImageProcessorTests
{
    private static readonly string TestDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin", "TestAssets", "WeaponProcessor");

    private GenshinWeaponImageProcessor m_Processor;

    [SetUp]
    public void Setup()
    {
        m_Processor = new GenshinWeaponImageProcessor();
    }

    [Test]
    public void ShouldProcess_ReturnsTrue()
    {
        Assert.That(m_Processor.ShouldProcess, Is.True);
    }

    #region Unit Tests

    [Test]
    public void ProcessImage_WithInsufficientImages_ThrowsException()
    {
        // Create 2 valid streams
        using var icon = CreateTestImageWithShape(10, 10, 5);
        using var original = CreateTestImageWithShape(10, 10, 5);

        var streams = new[] { MatToStream(icon), MatToStream(original) };

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => m_Processor.ProcessImage(streams));
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    [Test]
    public void ProcessImage_WhenIconNotFound_ReturnsNullStream()
    {
        // Create mismatching images
        // Icon: Small square
        using var icon = CreateTestImageWithShape(50, 50, 20);
        // Original: Completely transparent, so it won't match the icon shape
        using var original = new Mat(100, 100, MatType.CV_8UC4, Scalar.All(0));
        using var ascended = new Mat(100, 100, MatType.CV_8UC4, Scalar.Red);

        var streams = new[] { MatToStream(icon), MatToStream(original), MatToStream(ascended) };

        try
        {
            var result = m_Processor.ProcessImage(streams);
            Assert.That(result, Is.EqualTo(Stream.Null));
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    [Test]
    public void ProcessImage_WhenIconFound_ReturnsProcessedImage()
    {
        // Create matching images
        // Icon: 50x50, center 20x20 opaque
        using var icon = CreateTestImageWithShape(50, 50, 20);

        // Original: 100x100, center 40x40 opaque (2x scale)
        using var original = CreateTestImageWithShape(100, 100, 40);

        // Ascended: 100x100, solid blue (255, 0, 0, 255)
        using var ascended = new Mat(100, 100, MatType.CV_8UC4, new Scalar(255, 0, 0, 255));

        var streams = new[] { MatToStream(icon), MatToStream(original), MatToStream(ascended) };

        try
        {
            var result = m_Processor.ProcessImage(streams);

            Assert.That(result, Is.Not.EqualTo(Stream.Null));
            Assert.That(result.Length, Is.GreaterThan(0));

            // Verify result is a valid image
            using var resultMat = Cv2.ImDecode(StreamToBytes(result), ImreadModes.Unchanged);
            Assert.That(resultMat.Width, Is.EqualTo(200));
            Assert.That(resultMat.Height, Is.EqualTo(200));

            // Check center pixel color (should be blue from ascended)
            var centerPixel = resultMat.At<Vec4b>(100, 100);
            using (Assert.EnterMultipleScope())
            {
                // OpenCV uses BGRA
                Assert.That(centerPixel.Item0, Is.EqualTo(255), "Blue channel mismatch");
                Assert.That(centerPixel.Item1, Is.EqualTo(0), "Green channel mismatch");
                Assert.That(centerPixel.Item2, Is.EqualTo(0), "Red channel mismatch");
                Assert.That(centerPixel.Item3, Is.EqualTo(255), "Alpha channel mismatch");
            }
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("icon_sword.png", "original_sword.png", "ascended_sword.png", "golden_sword.png")]
    [TestCase("icon_polearm.png", "original_polearm.png", "ascended_polearm.png", "golden_polearm.png")]
    [TestCase("icon_claymore.png", "original_claymore.png", "ascended_claymore.png", "golden_claymore.png")]
    [TestCase("icon_bow.png", "original_bow.png", "ascended_bow.png", "golden_bow.png")]
    public void ProcessImage_ShouldMatchGoldenImage(string iconFile, string originalFile, string ascendedFile, string goldenImage)
    {
        using var icon = File.OpenRead(Path.Combine(TestDirectory, iconFile));
        using var original = File.OpenRead(Path.Combine(TestDirectory, originalFile));
        using var ascended = File.OpenRead(Path.Combine(TestDirectory, ascendedFile));

        var golden = File.ReadAllBytes(Path.Combine(TestDirectory, goldenImage));

        using var stream = m_Processor.ProcessImage([icon, original, ascended]);

        MemoryStream memoryStream = new();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "WeaponProcessor");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, goldenImage.Replace("golden", "output"));
        File.WriteAllBytes(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, goldenImage);
        File.WriteAllBytes(outputGoldenImagePath, golden);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(golden));
    }

    #endregion

    #region Helpers
    private static Mat CreateTestImageWithShape(int width, int height, int shapeSize)
    {
        var mat = new Mat(height, width, MatType.CV_8UC4, Scalar.All(0)); // Transparent

        // Draw opaque rectangle in center
        var x = (width - shapeSize) / 2;
        var y = (height - shapeSize) / 2;

        // White rectangle, fully opaque
        Cv2.Rectangle(mat, new Rect(x, y, shapeSize, shapeSize), new Scalar(255, 255, 255, 255), -1);

        return mat;
    }

    private static Stream MatToStream(Mat mat)
    {
        return new MemoryStream(mat.ImEncode(".png"));
    }

    private static byte[] StreamToBytes(Stream stream)
    {
        if (stream is MemoryStream ms) return ms.ToArray();
        using var temp = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(temp);
        return temp.ToArray();
    }


    [Test]
    [TestCase("icon_sword.png", "original_sword.png", "ascended_sword.png", "output_sword.png")]
    [TestCase("icon_polearm.png", "original_polearm.png", "ascended_polearm.png", "output_polearm.png")]
    [TestCase("icon_claymore.png", "original_claymore.png", "ascended_claymore.png", "output_claymore.png")]
    [TestCase("icon_bow.png", "original_bow.png", "ascended_bow.png", "output_bow.png")]
    public void GenerateImage(string iconFile, string originalFile, string ascendedFile, string outputFile)
    {
        var icon = File.OpenRead(Path.Combine(TestDirectory, iconFile));
        var original = File.OpenRead(Path.Combine(TestDirectory, originalFile));
        var ascended = File.OpenRead(Path.Combine(TestDirectory, ascendedFile));

        var resultStream = m_Processor.ProcessImage([icon, original, ascended]);

        Assert.That(resultStream, Is.Not.Null);

        var resultFile = File.Create(Path.Combine(TestDirectory, outputFile));
        resultStream.Position = 0;
        resultStream.CopyTo(resultFile);
        resultFile.Flush();
        resultFile.Close();
    }

    #endregion
}
