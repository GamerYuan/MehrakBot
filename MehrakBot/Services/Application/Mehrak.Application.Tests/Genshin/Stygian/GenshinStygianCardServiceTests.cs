#region

using System.Text.Json;
using Mehrak.Application.Genshin.Stygian;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Genshin.Stygian;

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
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>(),
            Mock.Of<IApplicationMetrics>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Stygian_TestData_1.json")]
    [TestCase("Stygian_TestData_2.json")]
    [TestCase("Stygian_TestData_3.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<StygianData>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<StygianData>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        var stream = await m_Service.GetCardAsync(cardContext);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"GenshinStygian_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinStygian_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        using var goldenStream = new MemoryStream(goldenImage);
        Assert.That(memoryStream, IsImage.IdenticalTo(goldenStream));
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

    [Explicit]
    [Test]
    [TestCase("Stygian_TestData_1.json")]
    [TestCase("Stygian_TestData_2.json")]
    [TestCase("Stygian_TestData_3.json")]
    public async Task GenerateGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<StygianData>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<StygianData>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        await using var stream = await m_Service.GetCardAsync(cardContext);

        using var fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        await stream.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
    }
}
