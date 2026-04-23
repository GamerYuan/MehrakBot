#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.EndGame;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrPureFictionCardServiceTests
{
    private HsrPureFictionCardService m_Service = null!;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_Service = new HsrPureFictionCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrPureFictionCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Pf_TestData_1.json")]
    [TestCase("Pf_TestData_2.json")]
    [TestCase("Pf_TestData_3.json")]
    public async Task GetCardAsync_PureFictionTestData_MatchesGoldenImage(string testDataFileName)
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
        cardContext.SetParameter("server", Mehrak.Domain.Enums.Server.Asia);

        var stream = await m_Service.GetCardAsync(cardContext);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    [Explicit]
    [Test]
    [TestCase("Pf_TestData_1.json", "Pf_GoldenImage_1.jpg")]
    [TestCase("Pf_TestData_2.json", "Pf_GoldenImage_2.jpg")]
    [TestCase("Pf_TestData_3.json", "Pf_GoldenImage_3.jpg")]
    public async Task GeneratePureFictionGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var testData = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
               File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", testDataFileName)));
        Assert.That(testData, Is.Not.Null);

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrEndInformation>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Mehrak.Domain.Enums.Server.Asia);

        var image = await m_Service.GetCardAsync(cardContext);

        await using var fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
            "TestAssets", goldenImageFileName));
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
            Level = 70
        };
    }
}

[Parallelizable(ParallelScope.Fixtures)]
public class HsrApocalypticShadowCardServiceTests
{
    private HsrApocalypticShadowCardService m_Service = null!;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_Service = new HsrApocalypticShadowCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrApocalypticShadowCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("As_TestData_1.json")]
    [TestCase("As_TestData_2.json")]
    [TestCase("As_TestData_3.json")]
    public async Task GetCardAsync_ApocalypticShadowTestData_MatchesGoldenImage(string testDataFileName)
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
        cardContext.SetParameter("server", Mehrak.Domain.Enums.Server.Asia);

        var stream = await m_Service.GetCardAsync(cardContext);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"HsrAs_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrAs_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    [Explicit]
    [Test]
    [TestCase("As_TestData_1.json", "As_GoldenImage_1.jpg")]
    [TestCase("As_TestData_2.json", "As_GoldenImage_2.jpg")]
    [TestCase("As_TestData_3.json", "As_GoldenImage_3.jpg")]
    public async Task GenerateApocalypticShadowGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var testData = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", testDataFileName)), JsonOptions);
        Assert.That(testData, Is.Not.Null);

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrEndInformation>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Mehrak.Domain.Enums.Server.Asia);

        var image = await m_Service.GetCardAsync(cardContext);

        await using var fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
            "TestAssets", goldenImageFileName));
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
            Level = 70
        };
    }
}