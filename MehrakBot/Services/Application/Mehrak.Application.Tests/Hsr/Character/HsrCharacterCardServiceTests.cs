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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    [TestCase("Acheron_ExUnlocked_TestData.json", "Character_Acheron_ExUnlocked_GoldenImage.jpg", "HsrCharacter_Acheron_ExUnlocked")]
    [TestCase("Acheron_ExLocked_TestData.json", "Character_Acheron_ExLocked_GoldenImage.jpg", "HsrCharacter_Acheron_ExLocked")]
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

    private static async Task<byte[]> RenderCharacterCardAsync(HsrCharacterCardService characterCardService,
        string testDataFileName)
    {
        var testDataPath = Path.Combine(TestDataPath, testDataFileName);
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var cardContext = new BaseCardGenerationContext<HsrCharacterInformation>(TestUserId, characterDetail!,
            GetTestUserGameData());
        cardContext.SetParameter("server", Server.Asia);

        await using var generatedImageStream = await characterCardService.GetCardAsync(cardContext);
        using MemoryStream memoryStream = new();
        await generatedImageStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
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
        await using var generatedImageStream = await characterCardService.GetCardAsync(cardContext);

        // The custom stream must change the portrait, not merely produce a nonempty card.
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

        // Rendering disposes this fixture's single scoped DbContext; use the existing stock reference.
        var stockImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
            "TestAssets", "Character_Stelle_GoldenImage.jpg"));
        var comparison = PerceptualImageComparator.Compare(stockImage, generatedImage.ToArray(),
            PerceptualImageComparator.DefaultDctSimilarityPercent);
        Assert.Multiple(() =>
        {
            Assert.That(comparison.DimensionsMatch, Is.True);
            Assert.That(comparison.IsEquivalent, Is.False, "The supplied red portrait must differ from stock artwork.");
        });
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

    [Test]
    public async Task GenerateCharacterCardAsync_WhenExRankStateChanges_TintsOnlyExIcon()
    {
        var (lockedRelicContext, lockedCardService) = await SetupTest();
        SeedRelicData(lockedRelicContext);
        var lockedBytes = await RenderCharacterCardAsync(lockedCardService, "Acheron_ExLocked_TestData.json");

        // GetCardAsync disposes the scoped DbContext, so each render needs its own service/context.
        // The factory shares one SQLite database, so the relic rows seeded above are still present.
        var (_, unlockedCardService) = await SetupTest();
        var unlockedBytes = await RenderCharacterCardAsync(unlockedCardService, "Acheron_ExUnlocked_TestData.json");

        using var locked = Image.Load<Rgba32>(lockedBytes);
        using var unlocked = Image.Load<Rgba32>(unlockedBytes);

        // Ex slot: centre (900, 1115), radius 35, padding 5 -> 60x60 icon box at (870, 1085).
        // The 8 px margin follows the JPEG block grid so encoder bleed cannot reach the compared pixels.
        var exIcon = new Rectangle(870, 1085, 60, 60);
        const int jpegPadding = 8;
        var excludedLeft = exIcon.Left - jpegPadding;
        var excludedTop = exIcon.Top - jpegPadding;
        var excludedRight = exIcon.Right + jpegPadding;
        var excludedBottom = exIcon.Bottom + jpegPadding;
        var differingInsideExIcon = 0;
        var differingOutsideExIcon = 0;

        Assert.That(locked.Size, Is.EqualTo(unlocked.Size));
        for (var y = 0; y < locked.Height; y++)
        {
            for (var x = 0; x < locked.Width; x++)
            {
                if (locked[x, y].Equals(unlocked[x, y])) continue;
                if (x >= excludedLeft && x < excludedRight && y >= excludedTop && y < excludedBottom)
                    differingInsideExIcon++;
                else
                    differingOutsideExIcon++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(differingOutsideExIcon, Is.Zero,
                "Only the Ex rank icon may change between locked and unlocked states.");
            Assert.That(differingInsideExIcon, Is.GreaterThan(0),
                "The unlocked Ex rank must be tinted differently from the locked state.");
        });

        // The accent ring comes from the shared bounded-icon path and must stay identical.
        // These ring pixels sit in JPEG blocks that do not contain the 60x60 icon box, so any
        // difference here means the tint leaked onto the border rather than encoder bleed.
        foreach (var ringPoint in new[]
                 {
                     new Point(900, 1079), new Point(900, 1152), new Point(863, 1115), new Point(936, 1115)
                 })
        {
            Assert.That(locked[ringPoint.X, ringPoint.Y].Equals(unlocked[ringPoint.X, ringPoint.Y]), Is.True,
                $"Ex rank border pixel at {ringPoint} must not be tinted.");
        }
    }

    [Test]
    public void ApplyExRankAccentTint_MixesAccentAndPreservesAlpha()
    {
        using var image = new Image<Rgba32>(3, 1);
        image[0, 0] = new Rgba32(255, 255, 255, 255);
        image[1, 0] = new Rgba32(10, 20, 30, 128);
        image[2, 0] = new Rgba32(10, 20, 30, 0);

        HsrCharacterCardService.ApplyExRankAccentTint(image, Color.ParseHex("#bb4cd3"));

        Assert.Multiple(() =>
        {
            Assert.That(image[0, 0].R, Is.EqualTo(231).Within(1));
            Assert.That(image[0, 0].G, Is.EqualTo(192).Within(1));
            Assert.That(image[0, 0].B, Is.EqualTo(240).Within(1));
            Assert.That(image[0, 0].A, Is.EqualTo(255));
            Assert.That(image[1, 0].A, Is.EqualTo(128));
            Assert.That(image[2, 0].A, Is.EqualTo(0));
        });
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
            CardBenchmarkMetrics.Create());
        await characterCardService.InitializeAsync();

        return (relicContext, characterCardService);
    }

    private static void SeedRelicData(RelicDbContext relicContext)
    {
        relicContext.HsrRelics.AddRange(
            new HsrRelicModel { SetId = 116, SetName = "Prisoner in Deep Confinement" },
            new HsrRelicModel { SetId = 117, SetName = "Pioneer Diver of Dead Waters" },
            new HsrRelicModel { SetId = 118, SetName = "Watchmaker, Master of Dream Machinations" },
            new HsrRelicModel { SetId = 119, SetName = "Iron Cavalry Against the Scourge" },
            new HsrRelicModel { SetId = 307, SetName = "Talia: Kingdom of Banditry" },
            new HsrRelicModel { SetId = 310, SetName = "Broken Keel" },
            new HsrRelicModel { SetId = 314, SetName = "Izumo Gensei and Takama Divine Realm" }
        );
        relicContext.SaveChanges();
    }

    [Explicit]
    [Test]
    [TestCase("Stelle_TestData.json", "Character_Stelle_GoldenImage.jpg")]
    [TestCase("Stelle_NoEquip_NoRelic_TestData.json", "Character_Stelle_NoEquip_NoRelic_GoldenImage.jpg")]
    [TestCase("Stelle_Remembrance_TestData.json", "Character_Stelle_Remembrance_GoldenImage.jpg")]
    [TestCase("Yaoguang_Elation_TestData.json", "Character_Yaoguang_Elation_GoldenImage.jpg")]
    [TestCase("Acheron_ExUnlocked_TestData.json", "Character_Acheron_ExUnlocked_GoldenImage.jpg")]
    [TestCase("Acheron_ExLocked_TestData.json", "Character_Acheron_ExLocked_GoldenImage.jpg")]
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
    [TestCase("Acheron_ExUnlocked_TestData.json", "HsrCharacter_Acheron_ExUnlocked_Preview.jpg")]
    [TestCase("Acheron_ExLocked_TestData.json", "HsrCharacter_Acheron_ExLocked_Preview.jpg")]
    public async Task GenerateAcheronExPreview(string testDataFileName, string previewFileName)
    {
        var (relicContext, characterCardService) = await SetupTest();
        SeedRelicData(relicContext);

        var generatedImage = await RenderCharacterCardAsync(characterCardService, testDataFileName);

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllBytesAsync(Path.Combine(outputDirectory, previewFileName), generatedImage);
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
