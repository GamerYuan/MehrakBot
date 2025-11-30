using Mehrak.Application.Services.Genshin.Types;

namespace Mehrak.Application.Tests.Services.Genshin.Types;

[Parallelizable(ParallelScope.All)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class GenshinWeaponImageProcessorTests
{
    private static readonly string TestDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin", "TestAssets", "WeaponProcessor");

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

        var processor = new GenshinWeaponImageProcessor();
        using var stream = processor.ProcessImage([icon, original, ascended]);

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

    /*
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

        var processor = new GenshinWeaponImageProcessor();
        var resultStream = processor.ProcessImage([icon, original, ascended]);

        Assert.That(resultStream, Is.Not.Null);

        var resultFile = File.Create(Path.Combine(TestDirectory, outputFile));
        resultStream.Position = 0;
        resultStream.CopyTo(resultFile);
        resultFile.Flush();
        resultFile.Close();
    }
    */
}
