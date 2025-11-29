#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.Character;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage.jpg", "Stelle")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Stelle_NoEquip_NoRelic_GoldenImage.jpg", "StelleNoEquipNoRelic")]
    [TestCase("Stelle_Remembrance_TestData.json", "Stelle_Remembrance_GoldenImage.jpg", "StelleRemembrance")]
    public async Task GenerateCharacterCardAsync_ShouldMatchGoldenImage(string testDataFileName,
        string goldenImageFileName, string testName)
    {
        // Arrange
        (Mock<IRelicRepository>? relicRepositoryMock, HsrCharacterCardService? characterCardService) = await SetupTest();
        SetupRelicRepository(relicRepositoryMock);

        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        HsrCharacterInformation? characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        // Act
        Stream generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, testName);
    }

    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage_UnknownSet.jpg", "Stelle")]
    public async Task GenerateCharacterCardAsync_WithUnknownSet_ShouldMatchGoldenImage(string testDataFileName,
        string goldenImageFileName, string testName)
    {
        // Arrange
        (Mock<IRelicRepository>? relicRepositoryMock, HsrCharacterCardService? characterCardService) = await SetupTest();
        relicRepositoryMock.Setup(x => x.GetSetName(It.IsAny<int>())).ReturnsAsync(string.Empty);

        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        HsrCharacterInformation? characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        // Act
        Stream generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, testName);
    }

    private static GameProfileDto GetTestUserGameData()
    {
        return new GameProfileDto
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 70
        };
    }

    private static async Task AssertImageMatches(Stream generatedImageStream, string goldenImagePath, string testName)
    {
        Assert.That(generatedImageStream, Is.Not.Null, $"Generated image stream should not be null for {testName}");
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0),
            $"Generated image should have content for {testName}");

        // Read the generated image
        using MemoryStream memoryStream = new();
        await generatedImageStream.CopyToAsync(memoryStream);
        var generatedImageBytes = memoryStream.ToArray();

        // Compare basic properties
        Assert.That(generatedImageBytes, Is.Not.Empty,
            $"Generated image should have content for {testName}");

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        if (!File.Exists(goldenImagePath))
        {
            Console.WriteLine(
                $"Golden image not found at {goldenImagePath} for test {testName}. Generated image saved to {outputImagePath}");
            Assert.Fail($"Golden image not found at {goldenImagePath} for test {testName}. " +
                        "Please run the GenerateGoldenImage test to create golden images.");
        }

        // Read the golden image
        var goldenImageBytes = await File.ReadAllBytesAsync(goldenImagePath);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImageBytes);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImageBytes),
            $"Generated image should match golden image for {testName}");
    }

    private static async Task<(Mock<IRelicRepository>, HsrCharacterCardService)> SetupTest()
    {
        var relicRepositoryMock = new Mock<IRelicRepository>();

        var characterCardService = new HsrCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            relicRepositoryMock.Object,
            Mock.Of<ILogger<HsrCharacterCardService>>());
        await characterCardService.InitializeAsync();

        return (relicRepositoryMock, characterCardService);
    }

    private static void SetupRelicRepository(Mock<IRelicRepository> relicRepositoryMock)
    {
        relicRepositoryMock.Setup(x => x.GetSetName(116)).ReturnsAsync("Prisoner in Deep Confinement");
        relicRepositoryMock.Setup(x => x.GetSetName(118)).ReturnsAsync("Watchmaker, Master of Dream Machinations");
        relicRepositoryMock.Setup(x => x.GetSetName(119)).ReturnsAsync("Iron Cavalry Against the Scourge");
        relicRepositoryMock.Setup(x => x.GetSetName(307)).ReturnsAsync("Talia: Kingdom of Banditry");
        relicRepositoryMock.Setup(x => x.GetSetName(310)).ReturnsAsync("Broken Keel");
    }

    // To be used to generate golden image should the generation algorithm be updated
    /*
    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage.jpg")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Stelle_NoEquip_NoRelic_GoldenImage.jpg")]
    [TestCase("Stelle_Remembrance_TestData.json", "Stelle_Remembrance_GoldenImage.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var (relicRepositoryMock, characterCardService) = await SetupTest();
        SetupRelicRepository(relicRepositoryMock);

        var characterDetail =
            JsonSerializer.Deserialize<HsrCharacterInformation>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        var image = await characterCardService.GetCardAsync(cardContext);
        using var stream = new MemoryStream();
        await image.CopyToAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets",
            "Hsr", "TestAssets", goldenImageFileName), stream.ToArray());

        Assert.That(image, Is.Not.Null);
    }

    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage_UnknownSet.jpg")]
    public async Task GenerateGoldenImage_WithUnknownSet(string testDataFileName, string goldenImageFileName)
    {
        var (relicRepositoryMock, characterCardService) = await SetupTest();
        relicRepositoryMock.Setup(x => x.GetSetName(It.IsAny<int>())).ReturnsAsync(string.Empty);

        var characterDetail =
            JsonSerializer.Deserialize<HsrCharacterInformation>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        var image = await characterCardService.GetCardAsync(cardContext);
        using var stream = new MemoryStream();
        await image.CopyToAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets",
            "Hsr", "TestAssets", goldenImageFileName), stream.ToArray());

        Assert.That(image, Is.Not.Null);
    }
    */
}
