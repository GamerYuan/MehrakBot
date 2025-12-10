#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.Theater;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinTheaterCardServiceTests
{
    private GenshinTheaterCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_Service = new GenshinTheaterCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinTheaterCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Theater_TestData_1.json")]
    [TestCase("Theater_TestData_2.json")]
    [TestCase("Theater_TestData_3.json")]
    [TestCase("Theater_TestData_4.json")]
    [TestCase("Theater_TestData_5.json")]
    [TestCase("Theater_TestData_6.json")]
    [TestCase("Theater_TestData_7.json")]
    [TestCase("Theater_TestData_8.json")]
    [TestCase("Theater_TestData_9.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<GenshinTheaterInformation>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.SetParameter("constMap", GetTestConstDictionary());

        var stream = await m_Service.GetCardAsync(cardContext);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"GenshinTheater_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinTheater_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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
            Level = 60
        };
    }

    private static Dictionary<int, int> GetTestConstDictionary()
    {
        return new Dictionary<int, int>
        {
            { 10000032, 6 },
            { 10000037, 1 },
            { 10000089, 6 },
            { 10000112, 0 }
        };
    }

    /*
    [Test]
    [TestCase("Theater_TestData_1.json")]
    [TestCase("Theater_TestData_2.json")]
    [TestCase("Theater_TestData_3.json")]
    [TestCase("Theater_TestData_4.json")]
    [TestCase("Theater_TestData_5.json")]
    [TestCase("Theater_TestData_6.json")]
    [TestCase("Theater_TestData_7.json")]
    [TestCase("Theater_TestData_8.json")]
    [TestCase("Theater_TestData_9.json")]
    public async Task GenerateGoldenImage(string testData)
    {
        GenshinTheaterInformation? testData1 = await
            JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
    File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
         "Genshin", testData)));
        Assert.That(testData1, Is.Not.Null, "Test data should not be null");

        GameProfileDto userGameData = GetTestUserGameData();
 Stream stream = await m_Service.GetCardAsync(
        new GenshinEndGameGenerationContext<GenshinTheaterInformation>(
         TestUserId, 12, testData1!, Server.Asia, userGameData, GetTestConstDictionary()));
     FileStream fs = File.OpenWrite(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", testData.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));
        await stream.CopyToAsync(fs);
 await fs.FlushAsync();
    }
    */
}
