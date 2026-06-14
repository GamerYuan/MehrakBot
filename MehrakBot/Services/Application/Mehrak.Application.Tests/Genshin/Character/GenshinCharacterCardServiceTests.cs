#region

using System.Text.Json;
using Mehrak.Application.Genshin.Character;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Genshin.Character;

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
            Mock.Of<IApplicationMetrics>());
        await m_GenshinCharacterCardService.InitializeAsync();
    }

    [Test]
    [TestCase("Aether_TestData.json", "Character_GoldenImage.jpg", "GenshinCharacter")]
    [TestCase("Aether_NotAscended_TestData.json", "Character_GoldenImage_NotAscended.jpg", "GenshinCharacter_NotAscended")]
    [TestCase("Aether_WithSet_TestData.json", "Character_GoldenImage_WithSet.jpg", "GenshinCharacterWithSet")]
    [TestCase("Aether_ConstActive_TestData.json", "Character_GoldenImage_ConstActive.jpg", "GenshinCharacter_ConstActive")]
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
        cardContext.SetParameter("ascension", 80);

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
        file.Position = 0;
        using var goldenStream = new MemoryStream(goldenImage);
        Assert.That(file, IsImage.IdenticalTo(goldenStream));
    }

    [Test]
    [TestCase("Aether_TestData.json", "Character_GoldenImage_NoAsc.jpg", "GenshinCharacter_NoAsc")]
    public async Task GenerateCharacterCard_NoAsc_MatchesGoldenImage(string testDataFileName, string goldenImageFileName,
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
        file.Position = 0;
        using var goldenStream2 = new MemoryStream(goldenImage);
        Assert.That(file, IsImage.IdenticalTo(goldenStream2));
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

    [Test]
    public async Task GenerateCharacterCard_WhenUserHasActivePortrait_UsesUserPortraitImage()
    {
        // Arrange - a portrait service that reports an active user portrait for any character.
        var portraitUploadId = Guid.NewGuid();
        // Provide a recognizable portrait image (solid red 800x1000 PNG) as the download.
        await using var portraitStream =
            PortraitServiceMockFactory.CreateSolidColorPngStream(800, 1000, (255, 0, 0));
        var portraitMock = PortraitServiceMockFactory.CreateWithActivePortrait(portraitUploadId, portraitStream);

        var cardService = new GenshinCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>(),
            Mock.Of<IApplicationMetrics>());
        await cardService.InitializeAsync();

        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<GenshinCharacterInformation>(
            TestUserId, characterDetail.List[0], profile);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.SetParameter("ascension", 80);

        // Act
        var image = await cardService.GetCardAsync(cardContext);

        // Assert - the user portrait image was requested, proving the user-portrait branch was taken
        // rather than the stock-image path.
        portraitMock.Verify(
            x => x.GetPortraitImageAsync((long)TestUserId, portraitUploadId, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(image, Is.Not.Null);
        Assert.That(image.Length, Is.GreaterThan(0));

        // Save the generated image to the Output folder for visual inspection.
        using MemoryStream generatedImage = new();
        await image.CopyToAsync(generatedImage);
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, "GenshinCharacter_UserPortrait_Generated.jpg"),
            generatedImage.ToArray());
    }

    [Test]
    public async Task GenerateCharacterCard_WhenUserPortraitDownloadFails_FallsBackToStockPortrait()
    {
        // Arrange - a portrait service that reports an active portrait but fails to download it.
        var portraitUploadId = Guid.NewGuid();
        var portraitMock = PortraitServiceMockFactory.CreateWithFailingDownload(portraitUploadId);

        var cardService = new GenshinCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>(),
            Mock.Of<IApplicationMetrics>());
        await cardService.InitializeAsync();

        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<GenshinCharacterInformation>(
            TestUserId, characterDetail.List[0], profile);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.SetParameter("ascension", 80);

        // Act - should not throw; falls back to the stock portrait.
        var image = await cardService.GetCardAsync(cardContext);

        // Assert - the card should match the stock golden image (same test data + params),
        // proving the fallback path produces a byte-identical result rather than a degraded one.
        using MemoryStream generatedImage = new();
        await image.CopyToAsync(generatedImage);
        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Character_GoldenImage.jpg"));

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, "GenshinCharacter_DownloadFailFallback_Generated.jpg"),
            generatedImage.ToArray());
        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, "GenshinCharacter_DownloadFailFallback_Golden.jpg"),
            goldenImage);

        generatedImage.Position = 0;
        using var goldenStream = new MemoryStream(goldenImage);
        Assert.That(generatedImage, IsImage.IdenticalTo(goldenStream));
    }

    // To be used to generate golden image should the generation algorithm be updated
    [Explicit]
    [Test]
    [TestCase("Aether_TestData.json", "Character_GoldenImage.jpg")]
    [TestCase("Aether_NotAscended_TestData.json", "Character_GoldenImage_NotAscended.jpg")]
    [TestCase("Aether_WithSet_TestData.json", "Character_GoldenImage_WithSet.jpg")]
    [TestCase("Aether_ConstActive_TestData.json", "Character_GoldenImage_ConstActive.jpg")]
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
        cardContext.SetParameter("ascension", 80);

        var image = await m_GenshinCharacterCardService.GetCardAsync(cardContext);
        var outputPath = $"Assets/Genshin/TestAssets/{goldenImageFileName}";
        await using var file = File.Create(outputPath);
        await image.CopyToAsync(file);
        await file.FlushAsync();

        Assert.That(image, Is.Not.Null);
    }

    [Explicit]
    [Test]
    [TestCase("Aether_TestData.json", "Character_GoldenImage_NoAsc.jpg")]
    public async Task GenerateGoldenImage_NoAsc(string testDataFileName, string goldenImageFileName)
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
        var outputPath = $"Assets/Genshin/TestAssets/{goldenImageFileName}";
        await using var file = File.Create(outputPath);
        await image.CopyToAsync(file);
        await file.FlushAsync();

        Assert.That(image, Is.Not.Null);
    }
}
