using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Hsr.Anomaly;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Services.Hsr.Anomaly;

internal class HsrAnomalyCardServiceTests
{
    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");
    private static readonly string TestAssetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets");

    private HsrAnomalyCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = new HsrAnomalyCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrAnomalyCardService>>());
        await m_Service.InitializeAsync();
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

    [Explicit]
    [Test]
    [TestCase("Anomaly_TestData_1.json", "Anomaly_GoldenImage_1.jpg")]
    public async Task GenerateGoldenImage(string fileName, string goldenImage)
    {
        var data = await JsonSerializer.DeserializeAsync<HsrAnomalyInformation>(File.OpenRead(Path.Combine(TestDataPath, fileName)));
        Assert.That(data, Is.Not.Null);

        var cardContext = new BaseCardGenerationContext<HsrAnomalyInformation>(TestUserId, data, GetTestUserGameData());
        cardContext.SetParameter("server", Server.Asia);

        var image = await m_Service.GetCardAsync(cardContext);

        using var fileStream = File.OpenWrite(Path.Combine(TestAssetsPath, goldenImage));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
        await fileStream.DisposeAsync();
    }
}
