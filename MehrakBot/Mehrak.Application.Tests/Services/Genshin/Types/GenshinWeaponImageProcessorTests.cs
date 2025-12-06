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

        var streams = new[] { MatToStream(icon) };

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
        using var ascended = new Mat(100, 100, MatType.CV_8UC4, Scalar.Red);

        var streams = new[] { MatToStream(icon), MatToStream(ascended) };

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

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("icon_sword.png", "ascended_sword.png", "golden_sword.png")]
    [TestCase("icon_polearm.png", "ascended_polearm.png", "golden_polearm.png")]
    [TestCase("icon_claymore.png", "ascended_claymore.png", "golden_claymore.png")]
    [TestCase("icon_bow.png", "ascended_bow.png", "golden_bow.png")]
    public void ProcessImage_ShouldMatchGoldenImage(string iconFile, string ascendedFile, string goldenImage)
    {
        using var icon = File.OpenRead(Path.Combine(TestDirectory, iconFile));
        using var ascended = File.OpenRead(Path.Combine(TestDirectory, ascendedFile));

        var golden = File.ReadAllBytes(Path.Combine(TestDirectory, goldenImage));

        using var stream = m_Processor.ProcessImage([icon, ascended]);

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

    /*
    [Test]
    [TestCase("icon_sword.png", "ascended_sword.png", "golden_sword.png")]
    [TestCase("icon_polearm.png", "ascended_polearm.png", "golden_polearm.png")]
    [TestCase("icon_claymore.png", "ascended_claymore.png", "golden_claymore.png")]
    [TestCase("icon_bow.png", "ascended_bow.png", "golden_bow.png")]
    public void GenerateImage(string iconFile, string ascendedFile, string outputFile)
    {
        var icon = File.OpenRead(Path.Combine(TestDirectory, iconFile));
        var ascended = File.OpenRead(Path.Combine(TestDirectory, ascendedFile));

        var resultStream = m_Processor.ProcessImage([icon, ascended]);

        Assert.That(resultStream, Is.Not.Null);

        var resultFile = File.Create(Path.Combine(TestDirectory, outputFile));
        resultStream.Position = 0;
        resultStream.CopyTo(resultFile);
        resultFile.Flush();
        resultFile.Close();
    }
    */
    #endregion
}
