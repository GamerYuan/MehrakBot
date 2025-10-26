﻿#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.CharList;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharListCardServiceTests
{
    private HsrCharListCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [SetUp]
    public void Setup()
    {
        m_Service = new HsrCharListCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrCharListCardService>>());
    }

    [Test]
    [TestCase("CharList_TestData_1.json")]
    public async Task GetCharListCardAsync_TestData_MatchesGoldenImage(string filename)
    {
        HsrBasicCharacterData? testData = await
            JsonSerializer.DeserializeAsync<HsrBasicCharacterData>(
                File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
                    "Hsr", filename)), JsonOptions);
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", filename.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        GameProfileDto userGameData = GetTestUserGameData();

        Stream image = await m_Service.GetCardAsync(
            new TestCardGenerationContext<IEnumerable<HsrCharacterInformation>>(TestUserId, testData!.AvatarList!,
                Server.Asia, userGameData));

        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"HsrCharList_Data{Path.GetFileNameWithoutExtension(filename).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrCharList_Data{Path.GetFileNameWithoutExtension(filename).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage), "Generated image should match the golden image");
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

    // [Test] [TestCase("CharList_TestData_1.json",
    // "CharList_GoldenImage_1.jpg")] public async Task
    // GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    // { HsrBasicCharacterData? testData = await
    // JsonSerializer.DeserializeAsync<HsrBasicCharacterData>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    // testDataFileName)), JsonOptions); Assert.That(testData, Is.Not.Null);
    //
    // GameProfileDto userGameData = GetTestUserGameData();
    //
    // Stream image = await m_Service.GetCardAsync( new
    // TestCardGenerationContext<IEnumerable<HsrCharacterInformation>>(TestUserId,
    // testData!.AvatarList!, Server.Asia, userGameData));
    //
    // await using FileStream fileStream =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", goldenImageFileName)); await image.CopyToAsync(fileStream);
    // await fileStream.FlushAsync();
    //
    // Assert.That(image, Is.Not.Null); }
}