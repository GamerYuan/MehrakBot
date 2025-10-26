#region

using System.Text.Json;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
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
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>());
        await m_GenshinCharacterCardService.InitializeAsync();
    }

    [Test]
    [TestCase("Aether_TestData.json", "GoldenImage.jpg", "GenshinCharacter")]
    [TestCase("Aether_WithSet_TestData.json", "GoldenImage_WithSet.jpg", "GenshinCharacterWithSet")]
    public async Task GenerateCharacterCard_MatchesGoldenImage(string testDataFileName, string goldenImageFileName,
        string outputPrefix)
    {
        // Arrange
        GenshinCharacterDetail? characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/{testDataFileName}"));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        // Act
        Stream image = await m_GenshinCharacterCardService.GetCardAsync(
            new TestCardGenerationContext<GenshinCharacterInformation>(TestUserId, characterDetail.List[0], Server.Asia,
                profile));
        using MemoryStream file = new();
        await image.CopyToAsync(file);

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory, $"{outputPrefix}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, file.ToArray());

        byte[] goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", goldenImageFileName));

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory, $"{outputPrefix}_Golden.jpg");
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

    // To be used to generate golden image should the generation algorithm be updated
    [Test]
    [TestCase("Aether_TestData.json", "GoldenImage.jpg")]
    [TestCase("Aether_WithSet_TestData.json", "GoldenImage_WithSet.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string
        goldenImageFileName)
    {
        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(await
                File.ReadAllTextAsync($"{TestDataPath}/Genshin/{testDataFileName}"));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        // Act
        var image = await m_GenshinCharacterCardService.GetCardAsync(
            new TestCardGenerationContext<GenshinCharacterInformation>(TestUserId,
                characterDetail.List[0], Server.Asia, profile));
        using var file = new MemoryStream();
        await image.CopyToAsync(file);
        await
            File.WriteAllBytesAsync($"Assets/Genshin/TestAssets/{goldenImageFileName}", file.ToArray());

        Assert.That(image, Is.Not.Null);
    }
}