#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Genshin.Character;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData");

    private ImageRepository m_ImageRepository;

    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, new NullLogger<ImageRepository>());
    }

    [Test]
    public async Task GenerateCharacterCard_MatchesGoldenImage()
    {
        // Arrange
        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));
        Assert.That(characterDetail, Is.Not.Null);

        var service = new GenshinCharacterCardService(m_ImageRepository, new NullLogger<GenshinCharacterCardService>());

        // Act
        var image = await service.GenerateCharacterCardAsync(characterDetail.List[0], "Test");
        using var file = new MemoryStream();
        await image.CopyToAsync(file);

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, "GenshinCharacter_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, file.ToArray());

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "GoldenImage.jpg"));

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, "GenshinCharacter_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        // Assert
        Assert.That(file.ToArray(), Is.EqualTo(goldenImage));
    }

    [Test]
    public async Task GenerateCharacterCard_WithSet_MatchesGoldenImage()
    {
        // Arrange
        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_WithSet_TestData.json"));
        Assert.That(characterDetail, Is.Not.Null);

        var service = new GenshinCharacterCardService(m_ImageRepository, new NullLogger<GenshinCharacterCardService>());

        // Act
        var image = await service.GenerateCharacterCardAsync(characterDetail.List[0], "Test");
        using var file = new MemoryStream();
        await image.CopyToAsync(file);

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, "GenshinCharacterWithSet_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, file.ToArray());

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "GoldenImage_WithSet.jpg"));

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, "GenshinCharacterWithSet_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        // Assert
        Assert.That(file.ToArray(), Is.EqualTo(goldenImage));
    }

    // To be used to generate golden image should the generation algorithm be updated
    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     // Arrange
    //     var characterDetail =
    //         JsonSerializer.Deserialize<GenshinCharacterDetail>(
    //             await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));
    //     var characterDetail2 =
    //         JsonSerializer.Deserialize<GenshinCharacterDetail>(
    //             await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_WithSet_TestData.json"));
    //     Assert.That(characterDetail, Is.Not.Null);
    //     Assert.That(characterDetail2, Is.Not.Null);
    //
    //     var service = new GenshinCharacterCardService(m_ImageRepository, new NullLogger<GenshinCharacterCardService>());
    //
    //     // Act
    //     var image = await service.GenerateCharacterCardAsync(characterDetail.List[0], "Test");
    //     using var file = new MemoryStream();
    //     await image.CopyToAsync(file);
    //     await File.WriteAllBytesAsync($"Assets/Genshin/TestAssets/GoldenImage.jpg", file.ToArray());
    //
    //     var image2 = await service.GenerateCharacterCardAsync(characterDetail2.List[0], "Test");
    //     using var file2 = new MemoryStream();
    //     await image2.CopyToAsync(file2);
    //     await File.WriteAllBytesAsync($"Assets/Genshin/TestAssets/GoldenImage_WithSet.jpg", file2.ToArray());
    //
    //     // Assert
    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(image, Is.Not.Null);
    //         Assert.That(image2, Is.Not.Null);
    //     });
    // }
}
