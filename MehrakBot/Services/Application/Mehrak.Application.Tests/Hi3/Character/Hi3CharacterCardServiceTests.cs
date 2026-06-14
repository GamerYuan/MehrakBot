using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Hi3.Character;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Hi3.Character;

internal class Hi3CharacterCardServiceTests
{

    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hi3");

    private Hi3CharacterCardService m_CharacterCardService;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_CharacterCardService = new Hi3CharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            PortraitServiceMockFactory.CreateEmpty(),
            Mock.Of<ILogger<Hi3CharacterCardService>>(),
            Mock.Of<IApplicationMetrics>());
        await m_CharacterCardService.InitializeAsync();
    }

    [Test]
    [TestCase("Character_TestData_1.json", "Character_GoldenImage_1.jpg", "Hi3Character_1")]
    [TestCase("Character_TestData_2.json", "Character_GoldenImage_2.jpg", "Hi3Character_2")]
    public async Task GenerateCharacterCardAsync_ShouldMatchGoldenImage(string testDataFileName,
       string goldenImageFileName, string testName)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };


        // Arrange
        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hi3", "TestAssets", goldenImageFileName);
        var characterDetail = JsonSerializer.Deserialize<Hi3CharacterDetail>(
            await File.ReadAllTextAsync(testDataPath), options);
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Hi3Server.SEA);

        // Act
        var generatedImageStream = await m_CharacterCardService.GetCardAsync(cardContext);

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, testName);
    }

    [Test]
    public async Task GetCardAsync_WhenNoCostumeImageExists_ThrowsCommandException()
    {
        // Arrange
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var characterDetail = JsonSerializer.Deserialize<Hi3CharacterDetail>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, "Character_TestData_1.json")), options);
        Assert.That(characterDetail, Is.Not.Null);

        var imageRepositoryMock = new Mock<IImageRepository>();

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        imageRepositoryMock.Setup(x => x.DownloadFileToStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stream.Null);
        imageRepositoryMock.Setup(x => x.DownloadFileToStreamAsync(FileNameFormat.Hi3.BackgroundName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await S3TestHelper.Instance.ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hi3.BackgroundName));
        imageRepositoryMock.Setup(x => x.DownloadFileToStreamAsync(FileNameFormat.Hi3.StigmataSlotName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await S3TestHelper.Instance.ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hi3.StigmataSlotName));
        imageRepositoryMock.Setup(x => x.DownloadFileToStreamAsync(FileNameFormat.Hi3.StarIconName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await S3TestHelper.Instance.ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hi3.StarIconName));

        foreach (var rank in new[] { 1, 2, 3, 4, 5 })
        {
            var rankFileName = string.Format(FileNameFormat.Hi3.RankName, rank);
            imageRepositoryMock.Setup(x => x.DownloadFileToStreamAsync(rankFileName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(await S3TestHelper.Instance.ImageRepository.DownloadFileToStreamAsync(rankFileName));
        }

        var service = new Hi3CharacterCardService(
            imageRepositoryMock.Object,
            PortraitServiceMockFactory.CreateEmpty(),
            Mock.Of<ILogger<Hi3CharacterCardService>>(),
            Mock.Of<IApplicationMetrics>());

        await service.InitializeAsync();

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(TestUserId, characterDetail!, profile);
        cardContext.SetParameter("server", Hi3Server.SEA);

        // Act & Assert
        var ex = Assert.ThrowsAsync<CommandException>(async () => await service.GetCardAsync(cardContext));
        Assert.That(ex!.Message, Is.EqualTo("No splash art image found for character"));
    }

    [Test]
    public async Task GenerateCharacterCardAsync_WhenUserHasActivePortrait_UsesUserPortraitImage()
    {
        // Arrange - a portrait service that reports an active user portrait for any character.
        var portraitUploadId = Guid.NewGuid();
        await using var portraitStream =
            PortraitServiceMockFactory.CreateSolidColorPngStream(800, 1000, (255, 0, 0));
        var portraitMock = PortraitServiceMockFactory.CreateWithActivePortrait(portraitUploadId, portraitStream);

        var cardService = new Hi3CharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            portraitMock.Object,
            Mock.Of<ILogger<Hi3CharacterCardService>>(),
            Mock.Of<IApplicationMetrics>());
        await cardService.InitializeAsync();

        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var characterDetail = JsonSerializer.Deserialize<Hi3CharacterDetail>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, "Character_TestData_1.json")), options);
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Hi3Server.SEA);

        // Act
        var generatedImageStream = await cardService.GetCardAsync(cardContext);

        // Assert - the user portrait image was requested, proving the user-portrait branch was taken
        // rather than the stock-image path.
        portraitMock.Verify(
            x => x.GetPortraitImageAsync((long)TestUserId, portraitUploadId, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(generatedImageStream, Is.Not.Null);
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0));

        // Save the generated image to the Output folder for visual inspection.
        generatedImageStream.Position = 0;
        using MemoryStream generatedImage = new();
        await generatedImageStream.CopyToAsync(generatedImage);
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, "Hi3Character_UserPortrait_Generated.jpg"),
            generatedImage.ToArray());
    }

    [Test]
    public async Task GenerateCharacterCardAsync_WhenUserPortraitDownloadFails_FallsBackToStockPortrait()
    {
        // Arrange - a portrait service that reports an active portrait but fails to download it.
        var portraitUploadId = Guid.NewGuid();
        var portraitMock = PortraitServiceMockFactory.CreateWithFailingDownload(portraitUploadId);

        var cardService = new Hi3CharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            portraitMock.Object,
            Mock.Of<ILogger<Hi3CharacterCardService>>(),
            Mock.Of<IApplicationMetrics>());
        await cardService.InitializeAsync();

        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var testDataPath = Path.Combine(TestDataPath, "Character_TestData_1.json");
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hi3", "TestAssets", "Character_GoldenImage_1.jpg");
        var characterDetail = JsonSerializer.Deserialize<Hi3CharacterDetail>(
            await File.ReadAllTextAsync(testDataPath), options);
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Hi3Server.SEA);

        // Act - should not throw; falls back to the stock portrait.
        var generatedImageStream = await cardService.GetCardAsync(cardContext);

        // Assert - the card should match the stock golden image (same test data + params),
        // proving the fallback path produces a byte-identical result rather than a degraded one.
        await AssertImageMatches(generatedImageStream, goldenImagePath, "Hi3Character_DownloadFailFallback");
    }

    private static GameProfileDto GetTestUserGameData()
    {
        return new GameProfileDto
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 88
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

        memoryStream.Position = 0;
        using var goldenStream = new MemoryStream(goldenImageBytes);
        Assert.That(memoryStream, IsImage.IdenticalTo(goldenStream),
            $"Generated image should match golden image for {testName}");
    }

    [Explicit]
    [Test]
    [TestCase("Character_TestData_1.json", "Character_GoldenImage_1.jpg")]
    [TestCase("Character_TestData_2.json", "Character_GoldenImage_2.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var characterDetail =
            JsonSerializer.Deserialize<Hi3CharacterDetail>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)), options);
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<Hi3CharacterDetail>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Hi3Server.SEA);

        var image = await m_CharacterCardService.GetCardAsync(cardContext);
        using var stream = new MemoryStream();
        await image.CopyToAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets",
            "Hi3", "TestAssets", goldenImageFileName), stream.ToArray());

        Assert.That(image, Is.Not.Null);
    }

}
