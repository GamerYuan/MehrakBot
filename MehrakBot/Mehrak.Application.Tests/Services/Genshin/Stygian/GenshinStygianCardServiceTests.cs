﻿#region

using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.Stygian;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinStygianCardServiceTests
{
    private GenshinStygianCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_Service = new GenshinStygianCardService(
            MongoTestHelper.Instance.ImageRepository,
 Mock.Of<ILogger<GenshinStygianCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Stygian_TestData_1.json")]
    [TestCase("Stygian_TestData_2.json")]
    [TestCase("Stygian_TestData_3.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        StygianData? testData =
            await JsonSerializer.DeserializeAsync<StygianData>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        GameProfileDto userGameData = GetTestUserGameData();

        Stream stream = await m_Service.GetCardAsync(
      new TestCardGenerationContext<StygianData>(TestUserId, testData!, Server.Asia, userGameData));
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"GenshinStygian_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinStygian_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    private static GameProfileDto GetTestUserGameData()
    {
        return new GameProfileDto
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 60,
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

    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     var testData1 = await JsonSerializer.DeserializeAsync<StygianData>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
    //         "Genshin", "Stygian_TestData_1.json")));
    //     var testData2 = await JsonSerializer.DeserializeAsync<StygianData>(
    //      File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
    //         "Genshin", "Stygian_TestData_2.json")));
    //     var testData3 = await JsonSerializer.DeserializeAsync<StygianData>(
    //     File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
    //      "Genshin", "Stygian_TestData_3.json")));
    //
    //     var userGameData = GetTestUserGameData();
    //
    //   var image1 = await m_Service.GetCardAsync(
    //    new TestCardGenerationContext<StygianData>(TestUserId, testData1!, Server.Asia, userGameData));
    //     var image2 = await m_Service.GetCardAsync(
    //       new TestCardGenerationContext<StygianData>(TestUserId, testData2!, Server.Asia, userGameData));
    //     var image3 = await m_Service.GetCardAsync(
    //       new TestCardGenerationContext<StygianData>(TestUserId, testData3!, Server.Asia, userGameData));
    //
    //     Assert.Multiple(() =>
    //{
    //         Assert.That(image1, Is.Not.Null);
    //         Assert.That(image2, Is.Not.Null);
    //         Assert.That(image3, Is.Not.Null);
    //  });
    //
    //     await using var fileStream1 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //      "TestAssets", "Stygian_GoldenImage_1.jpg"));
    //     await using var fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "Stygian_GoldenImage_2.jpg"));
    //  await using var fileStream3 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "Stygian_GoldenImage_3.jpg"));
    //
    //     await image1.CopyToAsync(fileStream1);
    //     await image2.CopyToAsync(fileStream2);
    //     await image3.CopyToAsync(fileStream3);
    // }
}
