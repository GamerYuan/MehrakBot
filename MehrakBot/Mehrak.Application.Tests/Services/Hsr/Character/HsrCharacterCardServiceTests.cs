#region

using System.Text.Json;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
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

    private Mock<IRelicRepository> m_HsrRelicRepositoryMock;
    private HsrCharacterCardService m_HsrCharacterCardService;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_HsrRelicRepositoryMock = new Mock<IRelicRepository>();

        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(116)).ReturnsAsync("Prisoner in Deep Confinement");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(118)).ReturnsAsync("Watchmaker, Master of Dream Machinations");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(119)).ReturnsAsync("Iron Cavalry Against the Scourge");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(307)).ReturnsAsync("Talia: Kingdom of Banditry");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(310)).ReturnsAsync("Broken Keel");

        m_HsrCharacterCardService = new HsrCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            m_HsrRelicRepositoryMock.Object,
            Mock.Of<ILogger<HsrCharacterCardService>>());
        await m_HsrCharacterCardService.InitializeAsync();
    }

    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage.jpg", "Stelle")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Stelle_NoEquip_NoRelic_GoldenImage.jpg", "StelleNoEquipNoRelic")]
    [TestCase("Stelle_Remembrance_TestData.json", "Stelle_Remembrance_GoldenImage.jpg", "StelleRemembrance")]
    public async Task GenerateCharacterCardAsync_ShouldMatchGoldenImage(string testDataFileName,
        string goldenImageFileName, string testName)
    {
        // Arrange
        string testDataPath = Path.Combine(TestDataPath, testDataFileName);
        string goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        HsrCharacterInformation? characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        // Act
        Stream generatedImageStream = await m_HsrCharacterCardService.GetCardAsync(
            new TestCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, Server.Asia, profile));

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

    private class TestCardGenerationContext<T> : ICardGenerationContext<T>
    {
        public ulong UserId { get; }
        public T Data { get; }
        public Server Server { get; }
        public GameProfileDto GameProfile { get; }

        public TestCardGenerationContext(ulong userId, T data, Server server, GameProfileDto gameProfile)
        {
            UserId = userId;
            Data = data;
            Server = server;
            GameProfile = gameProfile;
        }
    }

    private static async Task AssertImageMatches(Stream generatedImageStream, string goldenImagePath, string testName)
    {
        Assert.That(generatedImageStream, Is.Not.Null, $"Generated image stream should not be null for {testName}");
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0),
            $"Generated image should have content for {testName}");

        // Read the generated image
        using MemoryStream memoryStream = new();
        await generatedImageStream.CopyToAsync(memoryStream);
        byte[] generatedImageBytes = memoryStream.ToArray();

        // Compare basic properties
        Assert.That(generatedImageBytes, Is.Not.Empty,
            $"Generated image should have content for {testName}");

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        if (!File.Exists(goldenImagePath))
        {
            Console.WriteLine(
                $"Golden image not found at {goldenImagePath} for test {testName}. Generated image saved to {outputImagePath}");
            Assert.Fail($"Golden image not found at {goldenImagePath} for test {testName}. " +
                        "Please run the GenerateGoldenImage test to create golden images.");
        }

        // Read the golden image
        byte[] goldenImageBytes = await File.ReadAllBytesAsync(goldenImagePath);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImageBytes);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImageBytes),
            $"Generated image should match golden image for {testName}");
    }

    // To be used to generate golden image should the generation algorithm be updated
    /*
    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage.jpg")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Stelle_NoEquip_NoRelic_GoldenImage.jpg")]
    [TestCase("Stelle_Remembrance_TestData.json", "Stelle_Remembrance_GoldenImage.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var characterDetail =
            JsonSerializer.Deserialize<HsrCharacterInformation>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        var image = await m_HsrCharacterCardService.GetCardAsync(new
            TestCardGenerationContext<HsrCharacterInformation>(TestUserId,
            characterDetail, Server.Asia, profile));
        using var stream = new MemoryStream();
        await image.CopyToAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets",
            "Hsr", "TestAssets", goldenImageFileName), stream.ToArray());

        Assert.That(image, Is.Not.Null);
    }
    */
}