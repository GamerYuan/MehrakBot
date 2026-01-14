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
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrAnomalyCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Anomaly_TestData_1.json", "Anomaly_GoldenImage_1.jpg", "HsrAnomaly_Data1")]
    [TestCase("Anomaly_TestData_2.json", "Anomaly_GoldenImage_2.jpg", "HsrAnomaly_Data2")]
    public async Task GenerateAnomalyCardAsync_MatchesGoldenImage(string fileName, string goldenImageName, string testName)
    {
        using var dataStream = File.OpenRead(Path.Combine(TestDataPath, fileName));
        var data = await JsonSerializer.DeserializeAsync<HsrAnomalyInformation>(dataStream);
        Assert.That(data, Is.Not.Null);

        var cardContext = new BaseCardGenerationContext<HsrAnomalyInformation>(TestUserId, data, GetTestUserGameData());
        cardContext.SetParameter("server", Server.Asia);

        using var image = await m_Service.GetCardAsync(cardContext);

        using var memStream = new MemoryStream();
        await image.CopyToAsync(memStream);
        memStream.Position = 0;
        var bytes = memStream.ToArray();

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(TestAssetsPath, goldenImageName));

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
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

    [Explicit]
    [Test]
    [TestCase("Anomaly_TestData_1.json", "Anomaly_GoldenImage_1.jpg")]
    [TestCase("Anomaly_TestData_2.json", "Anomaly_GoldenImage_2.jpg")]
    public async Task GenerateGoldenImage(string fileName, string goldenImage)
    {
        var data = await JsonSerializer.DeserializeAsync<HsrAnomalyInformation>(File.OpenRead(Path.Combine(TestDataPath, fileName)));
        Assert.That(data, Is.Not.Null);

        var cardContext = new BaseCardGenerationContext<HsrAnomalyInformation>(TestUserId, data, GetTestUserGameData());
        cardContext.SetParameter("server", Server.Asia);

        await using var image = await m_Service.GetCardAsync(cardContext);

        using var fileStream = File.OpenWrite(Path.Combine(TestAssetsPath, goldenImage));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
    }
}
