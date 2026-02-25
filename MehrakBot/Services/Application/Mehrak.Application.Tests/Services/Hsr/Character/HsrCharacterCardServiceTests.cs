#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.Character;

[Parallelizable(ParallelScope.Fixtures), FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;
    private TestDbContextFactory m_DbFactory = null!;

    [SetUp]
    public void Setup()
    {
        m_DbFactory = new TestDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        m_DbFactory.Dispose();
    }

    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage.jpg", "Stelle")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Stelle_NoEquip_NoRelic_GoldenImage.jpg", "StelleNoEquipNoRelic")]
    [TestCase("Stelle_Remembrance_TestData.json", "Stelle_Remembrance_GoldenImage.jpg", "StelleRemembrance")]
    [TestCase("Yaoguang_Elation_TestData.json", "Yaoguang_Elation_GoldenImage.jpg", "YaoguangElation")]
    public async Task GenerateCharacterCardAsync_ShouldMatchGoldenImage(string testDataFileName,
        string goldenImageFileName, string testName)
    {
        var (relicContext, characterCardService) = await SetupTest();
        SeedRelicData(relicContext);

        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        var generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        await AssertImageMatches(generatedImageStream, goldenImagePath, testName);
    }

    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage_UnknownSet.jpg", "Stelle")]
    public async Task GenerateCharacterCardAsync_WithUnknownSet_ShouldMatchGoldenImage(string testDataFileName,
        string goldenImageFileName, string testName)
    {
        var (_, characterCardService) = await SetupTest();

        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        var generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        await AssertImageMatches(generatedImageStream, goldenImagePath, testName);
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

    private static async Task AssertImageMatches(Stream generatedImageStream, string goldenImagePath, string testName)
    {
        Assert.That(generatedImageStream, Is.Not.Null, $"Generated image stream should not be null for {testName}");
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0),
            $"Generated image should have content for {testName}");

        using MemoryStream memoryStream = new();
        await generatedImageStream.CopyToAsync(memoryStream);
        var generatedImageBytes = memoryStream.ToArray();

        Assert.That(generatedImageBytes, Is.Not.Empty,
            $"Generated image should have content for {testName}");

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        if (!File.Exists(goldenImagePath))
        {
            Console.WriteLine(
                $"Golden image not found at {goldenImagePath} for test {testName}. Generated image saved to {outputImagePath}");
            Assert.Fail($"Golden image not found at {goldenImagePath} for test {testName}. " +
                        "Please run the GenerateGoldenImage test to create golden images.");
        }

        var goldenImageBytes = await File.ReadAllBytesAsync(goldenImagePath);

        var outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImageBytes);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImageBytes),
            $"Generated image should match golden image for {testName}");
    }

    private async Task<(RelicDbContext RelicContext, HsrCharacterCardService Service)> SetupTest()
    {
        var services = new ServiceCollection();
        var dbContext = m_DbFactory.CreateDbContext<RelicDbContext>();

        services.AddScoped(_ => dbContext);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var relicContext = provider.GetRequiredService<RelicDbContext>();

        var characterCardService = new HsrCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            scopeFactory,
            Mock.Of<ILogger<HsrCharacterCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());
        await characterCardService.InitializeAsync();

        return (relicContext, characterCardService);
    }

    private static void SeedRelicData(RelicDbContext relicContext)
    {
        relicContext.HsrRelics.AddRange(
            new HsrRelicModel { SetId = 116, SetName = "Prisoner in Deep Confinement" },
            new HsrRelicModel { SetId = 118, SetName = "Watchmaker, Master of Dream Machinations" },
            new HsrRelicModel { SetId = 119, SetName = "Iron Cavalry Against the Scourge" },
            new HsrRelicModel { SetId = 307, SetName = "Talia: Kingdom of Banditry" },
            new HsrRelicModel { SetId = 310, SetName = "Broken Keel" }
        );
        relicContext.SaveChanges();
    }

    [Explicit]
    [Test]
    [TestCase("Stelle_TestData.json", "Stelle_GoldenImage.jpg")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Stelle_NoEquip_NoRelic_GoldenImage.jpg")]
    [TestCase("Stelle_Remembrance_TestData.json", "Stelle_Remembrance_GoldenImage.jpg")]
    [TestCase("Yaoguang_Elation_TestData.json", "Yaoguang_Elation_GoldenImage.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var (relicContext, characterCardService) = await SetupTest();
        SeedRelicData(relicContext);

        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        var generatedImageStream = await characterCardService.GetCardAsync(cardContext);
        using var memoryStream = new MemoryStream();
        await generatedImageStream.CopyToAsync(memoryStream);

        var outputPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        await File.WriteAllBytesAsync(outputPath, memoryStream.ToArray());

        Assert.That(generatedImageStream, Is.Not.Null);
    }
}
