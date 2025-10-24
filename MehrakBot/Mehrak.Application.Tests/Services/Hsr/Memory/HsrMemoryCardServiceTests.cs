#region

using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.Memory;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrMemoryCardServiceTests
{
    private HsrMemoryCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_Service = new HsrMemoryCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrMemoryCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Moc_TestData_1.json")]
    [TestCase("Moc_TestData_2.json")]
    [TestCase("Moc_TestData_3.json")]
    [TestCase("Moc_TestData_4.json")]
    [TestCase("Moc_TestData_5.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        HsrMemoryInformation? testData =
            await JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        GameProfileDto userGameData = GetTestUserGameData();

        Stream stream = await m_Service.GetCardAsync(
            new TestCardGenerationContext<HsrMemoryInformation>(TestUserId, testData!, Server.Asia, userGameData));
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"HsrMoc_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrMoc_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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
            Level = 70,
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

    // [Test] [TestCase("Moc_TestData_1.json", "Moc_GoldenImage_1.jpg")]
    // [TestCase("Moc_TestData_2.json", "Moc_GoldenImage_2.jpg")]
    // [TestCase("Moc_TestData_3.json", "Moc_GoldenImage_3.jpg")]
    // [TestCase("Moc_TestData_4.json", "Moc_GoldenImage_4.jpg")]
    // [TestCase("Moc_TestData_5.json", "Moc_GoldenImage_5.jpg")] public async
    // Task GenerateGoldenImage(string testDataFileName, string
    // goldenImageFileName) { var testData = await
    // JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    // testDataFileName))); Assert.That(testData, Is.Not.Null);
    //
    // var userGameData = GetTestUserGameData();
    //
    // var image = await m_Service.GetCardAsync( new
    // TestCardGenerationContext<HsrMemoryInformation>(TestUserId, testData!,
    // Server.Asia, userGameData));
    //
    // await using var fileStream =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", goldenImageFileName)); await image.CopyToAsync(fileStream);
    //
    // Assert.That(image, Is.Not.Null); }
}
