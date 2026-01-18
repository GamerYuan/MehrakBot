using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Services.Hi3.Character;

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
            Mock.Of<ILogger<Hi3CharacterCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());
        await m_CharacterCardService.InitializeAsync();
    }

    [Test]
    [TestCase("Character_TestData_1.json", "Character_GoldenImage_1.jpg", "Character_1")]
    [TestCase("Character_TestData_2.json", "Character_GoldenImage_2.jpg", "Character_2")]
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

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImageBytes),
            $"Generated image should match golden image for {testName}");
    }

    /*
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

        GameProfileDto profile = GetTestUserGameData();

        var image = await m_CharacterCardService.GetCardAsync(new
            Hi3CardGenerationContext<Hi3CharacterDetail>(TestUserId,
            characterDetail, Hi3Server.SEA, profile));
        using var stream = new MemoryStream();
        await image.CopyToAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets",
            "Hi3", "TestAssets", goldenImageFileName), stream.ToArray());

        Assert.That(image, Is.Not.Null);
    }
    */
}
