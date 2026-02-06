using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Zzz.Tower;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Services.Zzz.Tower;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzTowerCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ZzzTowerCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "1300000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = new ZzzTowerCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<IApplicationMetrics>(),
            Mock.Of<ILogger<ZzzTowerCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Tower_TestData_1.json")]
    [TestCase("Tower_TestData_2.json")]
    [TestCase("Tower_TestData_3.json")]
    public async Task GetDefenseCardAsync_TestData_ShouldMatchGoldenImage(string testData)
    {
        var towerData =
            JsonSerializer.Deserialize<ZzzTowerData>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(towerData, Is.Not.Null);

        var userGameData = GetTestUserGameData();

        Dictionary<int, (int, int)> charMap = new()
        {
            { 1091, (60, 6) },
            { 1011, (60, 0) }
        };

        var context = new BaseCardGenerationContext<ZzzTowerData>(TestUserId, towerData, userGameData);
        context.SetParameter("server", Server.Asia);
        context.SetParameter("charMap", charMap);

        var image = await m_Service.GetCardAsync(context);

        using var goldenImage = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets", $"{Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage")}.jpg"));

        Assert.That(image, Is.Not.Null);

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"ZzzTower_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Generated.jpg");

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"ZzzTower_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Golden.jpg");
        await using (var outputFileStream = File.OpenWrite(outputImagePath))
        {
            await image.CopyToAsync(outputFileStream);
        }

        await using (var outputFileStream = File.OpenWrite(outputGoldenImagePath))
        {
            await goldenImage.CopyToAsync(outputFileStream);
        }

        goldenImage.Position = 0;
        image.Position = 0;
        Assert.That(image, Is.EqualTo(goldenImage), "Generated image should match the golden image");
    }

    [Explicit]
    [Test]
    [TestCase("Tower_TestData_1.json", "Tower_GoldenImage_1.jpg")]
    [TestCase("Tower_TestData_2.json", "Tower_GoldenImage_2.jpg")]
    [TestCase("Tower_TestData_3.json", "Tower_GoldenImage_3.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var towerData =
            JsonSerializer.Deserialize<ZzzTowerData>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(towerData, Is.Not.Null);

        var userGameData = GetTestUserGameData();

        Dictionary<int, (int, int)> charMap = new()
        {
            { 1091, (60, 6) },
            { 1011, (60, 0) }
        };

        var context = new BaseCardGenerationContext<ZzzTowerData>(TestUserId, towerData, userGameData);
        context.SetParameter("server", Server.Asia);
        context.SetParameter("charMap", charMap);

        var image = await m_Service.GetCardAsync(context);

        var fileStream = File.OpenWrite(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets", goldenImageFileName));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
        await fileStream.DisposeAsync();

        Assert.That(image, Is.Not.Null);
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
}
