#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.Character;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData");

    private GenshinCharacterCardService m_GenshinCharacterCardService;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_GenshinCharacterCardService = new GenshinCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());
        await m_GenshinCharacterCardService.InitializeAsync();
    }

    [Test]
    [TestCase("Aether_TestData.json", "GoldenImage.jpg", "GenshinCharacter")]
    [TestCase("Aether_NotAscended_TestData.json", "GoldenImage_NotAscended.jpg", "GenshinCharacter_NotAscended")]
    [TestCase("Aether_WithSet_TestData.json", "GoldenImage_WithSet.jpg", "GenshinCharacterWithSet")]
    [TestCase("Aether_ConstActive_TestData.json", "GoldenImage_ConstActive.jpg", "GenshinCharacter_ConstActive")]
    public async Task GenerateCharacterCard_MatchesGoldenImage(string testDataFileName, string goldenImageFileName,
        string outputPrefix)
    {
        // Arrange
        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/{testDataFileName}"));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<GenshinCharacterInformation>(TestUserId, characterDetail.List[0], profile);
        cardContext.SetParameter("server", Server.Asia);

        // Act
        var image = await m_GenshinCharacterCardService.GetCardAsync(cardContext);
        using MemoryStream file = new();
        await image.CopyToAsync(file);

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, $"{outputPrefix}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, file.ToArray());

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", goldenImageFileName));

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, $"{outputPrefix}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        // Assert
        Assert.That(file.ToArray(), Is.EqualTo(goldenImage));
    }

    private static GameProfileDto GetTestUserGameData()
    {
        return new GameProfileDto
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 60
        };
    }

    // To be used to generate golden image should the generation algorithm be updated
    [Explicit]
    [Test]
    [TestCase("Aether_TestData.json", "GoldenImage.jpg")]
    [TestCase("Aether_NotAscended_TestData.json", "GoldenImage_NotAscended.jpg")]
    [TestCase("Aether_WithSet_TestData.json", "GoldenImage_WithSet.jpg")]
    [TestCase("Aether_ConstActive_TestData.json", "GoldenImage_ConstActive.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(await
                File.ReadAllTextAsync($"{TestDataPath}/Genshin/{testDataFileName}"));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        // Act
        var cardContext = new BaseCardGenerationContext<GenshinCharacterInformation>(TestUserId,
            characterDetail.List[0], profile);
        cardContext.SetParameter("server", Server.Asia);

        var image = await m_GenshinCharacterCardService.GetCardAsync(cardContext);
        using var file = new MemoryStream();
        await image.CopyToAsync(file);
        await File.WriteAllBytesAsync($"Assets/Genshin/TestAssets/{goldenImageFileName}", file.ToArray());

        Assert.That(image, Is.Not.Null);
    }
}
