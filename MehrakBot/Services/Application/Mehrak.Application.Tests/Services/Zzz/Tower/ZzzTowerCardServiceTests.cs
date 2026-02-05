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


    [Explicit]
    [Test]
    [TestCase("Tower_TestData_1.json", "Tower_GoldenImage_1.jpg")]
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
