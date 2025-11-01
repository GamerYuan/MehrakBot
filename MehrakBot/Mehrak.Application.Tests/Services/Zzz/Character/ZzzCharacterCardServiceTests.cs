﻿#region

using System.Text.Json;
using Mehrak.Application.Services.Zzz.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Zzz.Character;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ZzzCharacterCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "1300000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = new ZzzCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzCharacterCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Jane_TestData.json", "Jane_GoldenImage.jpg", "Jane")]
    [TestCase("Miyabi_TestData.json", "Miyabi_GoldenImage.jpg", "Miyabi")]
    [TestCase("Yixuan_TestData.json", "Yixuan_GoldenImage.jpg", "Yixuan")]
    public async Task GenerateCharacterCardAsync_TestData_ShouldMatchGoldenImage(string testDataFileName,
        string goldenImageFileName, string testName)
    {
        ZzzFullAvatarData? characterDetail =
            JsonSerializer.Deserialize<ZzzFullAvatarData>(
                await File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(characterDetail, Is.Not.Null);

        byte[] goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets",
            goldenImageFileName));

        GameProfileDto profile = GetTestUserGameData();

        Stream image = await m_Service.GetCardAsync(
            new TestCardGenerationContext<ZzzFullAvatarData>(TestUserId, characterDetail, Server.Asia, profile));
        Assert.That(image, Is.Not.Null);

        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        byte[] generatedImageBytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImage), "Generated image should match the golden image");
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

    /*
    [Test]
    [TestCase("Jane_TestData.json", "Jane_GoldenImage.jpg")]
    [TestCase("Miyabi_TestData.json", "Miyabi_GoldenImage.jpg")]
    [TestCase("Yixuan_TestData.json", "Yixuan_GoldenImage.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string
        goldenImageFileName)
    {
        ZzzFullAvatarData? characterDetail =
            JsonSerializer.Deserialize<ZzzFullAvatarData>(await
            File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        Stream image = await m_Service.GetCardAsync(new
            TestCardGenerationContext<ZzzFullAvatarData>(TestUserId, characterDetail,
            Server.Asia, profile));

        FileStream fileStream = File.OpenWrite(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
        goldenImageFileName));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();

        Assert.That(image, Is.Not.Null);
    }
    */
}
