#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.EndGame;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrEndGameCardServiceTests
{
    private HsrEndGameCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [SetUp]
    public async Task Setup()
    {
        m_Service = new HsrEndGameCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrEndGameCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Pf_TestData_1.json")]
    [TestCase("Pf_TestData_2.json")]
    [TestCase("Pf_TestData_3.json")]
    public async Task GetEndGameCardAsync_PureFictionTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<HsrEndInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrEndInformation>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.SetParameter("mode", HsrEndGameMode.PureFiction);

        var stream = await m_Service.GetCardAsync(cardContext);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    [Test]
    [TestCase("As_TestData_1.json")]
    [TestCase("As_TestData_2.json")]
    [TestCase("As_TestData_3.json")]
    public async Task GetEndGameCardAsync_BossChallengeTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<HsrEndInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)), JsonOptions);
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrEndInformation>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.SetParameter("mode", HsrEndGameMode.ApocalypticShadow);

        var stream = await m_Service.GetCardAsync(cardContext);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"HsrAs_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrAs_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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
            Level = 70
        };
    }

    // [Test]
    // [TestCase("Pf_TestData_1.json", "Pf_GoldenImage_1.jpg")]
    // [TestCase("Pf_TestData_2.json", "Pf_GoldenImage_2.jpg")]
    // [TestCase("Pf_TestData_3.json", "Pf_GoldenImage_3.jpg")]
    // public async Task GeneratePureFictionGoldenImage(string testDataFileName, string goldenImageFileName)
    // {
    //  HsrEndInformation? testData = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", testDataFileName)));
    //     Assert.That(testData, Is.Not.Null);
    //
    //     GameProfileDto userGameData = GetTestUserGameData();
    //
    //   Stream image = await m_Service.GetCardAsync(
    //         new HsrEndGameGenerationContext(TestUserId, testData!, Server.Asia, userGameData, HsrEndGameMode.PureFiction));
    //
    //     await using FileStream fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    //         "TestAssets", goldenImageFileName));
    //     await image.CopyToAsync(fileStream);
    //
    //     Assert.That(image, Is.Not.Null);
    // }

    // [Test]
    // [TestCase("As_TestData_1.json", "As_GoldenImage_1.jpg")]
    // [TestCase("As_TestData_2.json", "As_GoldenImage_2.jpg")]
    // [TestCase("As_TestData_3.json", "As_GoldenImage_3.jpg")]
    // public async Task GenerateBossChallengeGoldenImage(string testDataFileName, string goldenImageFileName)
    // {
    //     HsrEndInformation? testData = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", testDataFileName)), JsonOptions);
    //     Assert.That(testData, Is.Not.Null);
    //
    //     GameProfileDto userGameData = GetTestUserGameData();
    //
    //     Stream image = await m_Service.GetCardAsync(
    //         new HsrEndGameGenerationContext(TestUserId, testData!, Server.Asia, userGameData, HsrEndGameMode.ApocalypticShadow));
    //
    //     await using FileStream fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    //         "TestAssets", goldenImageFileName));
    // await image.CopyToAsync(fileStream);
    //
    //     Assert.That(image, Is.Not.Null);
    // }
}
