﻿#region

using System.Text.Json;
using Mehrak.Application.Services.Zzz.Assault;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Zzz.Assault;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzAssaultCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ZzzAssaultCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "1300000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = new ZzzAssaultCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzAssaultCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Da_TestData_1.json")]
    [TestCase("Da_TestData_2.json")]
    public async Task GetAssaultCardAsync_TestData_ShouldMatchGoldenImage(string testData)
    {
        ZzzAssaultData? assaultData = JsonSerializer.Deserialize<ZzzAssaultData>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(assaultData, Is.Not.Null);

        byte[] goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets",
            $"{Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage")}.jpg"));

        GameProfileDto userGameData = GetTestUserGameData();

        Stream image = await m_Service.GetCardAsync(
            new TestCardGenerationContext<ZzzAssaultData>(TestUserId, assaultData, Server.Asia, userGameData));
        Assert.That(image, Is.Not.Null);

        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        byte[] generatedImageBytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"ZzzAssault_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"ZzzAssault_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Golden.jpg");
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

    // [Test] [TestCase("Da_TestData_1.json", "Da_GoldenImage_1.jpg")]
    // [TestCase("Da_TestData_2.json", "Da_GoldenImage_2.jpg")] public async
    // Task GenerateGoldenImage(string testDataFileName, string
    // goldenImageFileName) { ZzzAssaultData? assaultData =
    // JsonSerializer.Deserialize<ZzzAssaultData>( await
    // File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
    // Assert.That(assaultData, Is.Not.Null);
    //
    // GameProfileDto userGameData = GetTestUserGameData();
    //
    // Stream image = await m_Service.GetCardAsync( new
    // TestCardGenerationContext<ZzzAssaultData>(TestUserId, assaultData,
    // Server.Asia, userGameData));
    //
    // FileStream fileStream = File.OpenWrite(
    // Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
    // goldenImageFileName)); await image.CopyToAsync(fileStream); await fileStream.FlushAsync();
    //
    // Assert.That(image, Is.Not.Null); }
}