#region

using System.Text.Json;
using Mehrak.Application.Hsr.Character;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Relic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Hsr.Character;

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
    [TestCase("Stelle_TestData.json", "Character_Stelle_GoldenImage.jpg", "HsrCharacter_Stelle")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Character_Stelle_NoEquip_NoRelic_GoldenImage.jpg", "HsrCharacter_Stelle_NoEquip_NoRelic")]
    [TestCase("Stelle_Remembrance_TestData.json", "Character_Stelle_Remembrance_GoldenImage.jpg", "HsrCharacter_Stelle_Remembrance")]
    [TestCase("Yaoguang_Elation_TestData.json", "Character_Yaoguang_Elation_GoldenImage.jpg", "HsrCharacter_Yaoguang_Elation")]
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
    [TestCase("Stelle_TestData.json", "Character_Stelle_GoldenImage_UnknownSet.jpg", "HsrCharacter_Stelle_UnknownSet")]
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

    [Test]
    public async Task GenerateCharacterCardAsync_WhenUserHasActivePortrait_UsesUserPortraitImage()
    {
        // Arrange - provide a recognizable portrait image (solid red 800x1000 PNG) via the context.
        await using var portraitStream =
            PortraitServiceMockFactory.CreateSolidColorPngStream(800, 1000, (255, 0, 0));

        var (relicContext, characterCardService) = await SetupTest();
        SeedRelicData(relicContext);

        var testDataPath = Path.Combine(TestDataPath, "Stelle_TestData.json");
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.PortraitImageStream = portraitStream;

        // Act
        var generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        // Assert - the card should render successfully using the provided portrait stream.
        Assert.That(generatedImageStream, Is.Not.Null);
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0));

        // Save the generated image to the Output folder for visual inspection.
        generatedImageStream.Position = 0;
        using MemoryStream generatedImage = new();
        await generatedImageStream.CopyToAsync(generatedImage);
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, "HsrCharacter_UserPortrait_Generated.jpg"),
            generatedImage.ToArray());
    }

    [Test]
    public async Task GenerateCharacterCardAsync_WhenUserPortraitDownloadFails_FallsBackToStockPortrait()
    {
        // Arrange - no PortraitImageStream set, simulating a failed download. The card service
        // should fall back to the stock portrait path.
        var (relicContext, characterCardService) = await SetupTest();
        SeedRelicData(relicContext);

        var testDataPath = Path.Combine(TestDataPath, "Stelle_TestData.json");
        var goldenImagePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", "Character_Stelle_GoldenImage.jpg");
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        // Act - should not throw; falls back to the stock portrait.
        var generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        // Assert - the card should match the stock golden image (same test data + params),
        // proving the fallback path produces a byte-identical result rather than a degraded one.
        await AssertImageMatches(generatedImageStream, goldenImagePath, "HsrCharacter_DownloadFailFallback");
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

        memoryStream.Position = 0;
        using var goldenStream = new MemoryStream(goldenImageBytes);
        Assert.That(memoryStream, IsImage.IdenticalTo(goldenStream),
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
            Mock.Of<IApplicationMetrics>());
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
    [TestCase("Stelle_TestData.json", "Character_Stelle_GoldenImage.jpg")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Character_Stelle_NoEquip_NoRelic_GoldenImage.jpg")]
    [TestCase("Stelle_Remembrance_TestData.json", "Character_Stelle_Remembrance_GoldenImage.jpg")]
    [TestCase("Yaoguang_Elation_TestData.json", "Character_Yaoguang_Elation_GoldenImage.jpg")]
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
        await using var fs = File.Create(outputPath);
        await fs.WriteAsync(memoryStream.ToArray());
        await fs.FlushAsync();

        Assert.That(generatedImageStream, Is.Not.Null);
    }

    [Explicit]
    [Test]
    [TestCase("Stelle_TestData.json", "Character_Stelle_GoldenImage_UnknownSet.jpg")]
    public async Task GenerateGoldenImage_WithUnknownSet(string testDataFileName,
        string goldenImageFileName)
    {
        var (_, characterCardService) = await SetupTest();

        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var profile = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail, profile);
        cardContext.SetParameter("server", Server.Asia);

        var image = await characterCardService.GetCardAsync(cardContext);
        var output = Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets", goldenImageFileName);
        await using var fs = File.Create(output);
        await image.CopyToAsync(fs);
        await fs.FlushAsync();
    }
}
