﻿using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace Mehrak.Application.Tests.Services.Genshin.Abyss;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinAbyssCardServiceTests
{
    private GenshinAbyssCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_Service = new(MongoTestHelper.Instance.ImageRepository, Mock.Of<ILogger<GenshinAbyssCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Abyss_TestData_1.json")]
    [TestCase("Abyss_TestData_2.json")]
    [TestCase("Abyss_TestData_3.json")]
    public async Task GetAbyssCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        GenshinAbyssInformation? testData =
            await JsonSerializer.DeserializeAsync<GenshinAbyssInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        GameProfileDto profile = GetTestUserGameData();

        Stream stream =
            await m_Service.GetCardAsync(new(TestUserId, 12, testData, Domain.Enums.Server.Asia, profile, GetConstMap()));
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"GenshinAbyss_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinAbyss_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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

    private static Dictionary<int, int> GetConstMap()
    {
        return new Dictionary<int, int>
        {
            { 10000032, 6 },
            { 10000037, 1 },
            { 10000063, 2 },
            { 10000089, 6 },
            { 10000103, 3 },
            { 10000106, 0 },
            { 10000107, 4 },
            { 10000112, 5 }
        };
    }

    /*
    [Test]
    [TestCase("Abyss_TestData_1.json")]
    [TestCase("Abyss_TestData_2.json")]
    [TestCase("Abyss_TestData_3.json")]
    public async Task GenerateGoldenImage(string testDataFileName)
    {
        GenshinAbyssInformation? testData =
            await JsonSerializer.DeserializeAsync<GenshinAbyssInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        GameProfileDto profile = GetTestUserGameData();

        Stream stream =
            await m_Service.GetCardAsync(new(TestUserId, 12, testData, Domain.Enums.Server.Asia, profile, GetConstMap()));

        var fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        await stream.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
        await fileStream.DisposeAsync();
    }
    */
}
