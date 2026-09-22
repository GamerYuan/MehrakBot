#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Genshin;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Card;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Abstractions;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Tests.Genshin.Theater;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinTheaterCardServiceTests
{
    private TestableGenshinTheaterCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_Service = new TestableGenshinTheaterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinTheaterCardService>>(),
            CardBenchmarkMetrics.Create());
        await m_Service.InitializeAsync();
    }

    [TearDown]
    public void TearDown()
    {
        m_Service.Dispose();
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
    [TestCase("Theater_TestData_10.json")]
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
        var testDataNumber = Path.GetFileNameWithoutExtension(testDataFileName).Replace("Theater_TestData_", "");
        var outputImagePath = Path.Combine(outputDirectory,
            $"GenshinTheater_Data{testDataNumber}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinTheater_Data{testDataNumber}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        using var goldenStream = new MemoryStream(goldenImage);
        Assert.That(memoryStream, IsImage.IdenticalTo(goldenStream));
    }

    [Test]
    public async Task PreparedBackgrounds_AllDifficulties_MatchLegacyTransformAndAreIndependent()
    {
        (int Difficulty, string FileName)[] cases =
        [
            (1, "Theater_TestData_4.json"),
            (2, "Theater_TestData_3.json"),
            (3, "Theater_TestData_2.json"),
            (4, "Theater_TestData_1.json"),
            (5, "Theater_TestData_5.json")
        ];

        foreach (var (difficulty, fileName) in cases)
        {
            var context = await CreateContextAsync(fileName);
            using var expected = m_Service.CreateLegacyBackgroundForTest(difficulty);
            using var actual = m_Service.CreateBackgroundForTest(context);

            Assert.Multiple(() =>
            {
                Assert.That(actual.Width, Is.EqualTo(1900));
                Assert.That(actual.Height, Is.EqualTo(expected.Height));
            });

            using var expectedStream = new MemoryStream();
            using var actualStream = new MemoryStream();
            await expected.SaveAsPngAsync(expectedStream);
            await actual.SaveAsPngAsync(actualStream);
            Assert.That(actualStream, IsImage.PerceptuallyEquivalentTo(expectedStream));
        }

        var concurrentContext = await CreateContextAsync("Theater_TestData_5.json");
        var cloneTasks = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => m_Service.CreateBackgroundForTest(concurrentContext)))
            .ToArray();
        var clones = await Task.WhenAll(cloneTasks);
        try
        {
            using var expected = m_Service.CreateLegacyBackgroundForTest(5);
            clones[0].Mutate(ctx => ctx.Brightness(0.1f));

            using var expectedStream = new MemoryStream();
            using var untouchedStream = new MemoryStream();
            await expected.SaveAsPngAsync(expectedStream);
            await clones[1].SaveAsPngAsync(untouchedStream);
            Assert.That(untouchedStream, IsImage.PerceptuallyEquivalentTo(expectedStream));
        }
        finally
        {
            foreach (var clone in clones)
                clone.Dispose();
        }
    }

    [Test]
    public async Task ApplicationStartup_InitializationScopeDoesNotDisposeTheaterSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IImageRepository>(S3TestHelper.Instance.ImageRepository);
        services.AddSingleton<IApplicationMetrics>(CardBenchmarkMetrics.Create());
        services.AddGenshinApplicationServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var initializer = new AsyncInitializationHostedService(provider,
            Mock.Of<ILogger<AsyncInitializationHostedService>>());

        await initializer.StartAsync(CancellationToken.None);

        var service = provider.GetRequiredService<ICardService<GenshinTheaterInformation>>();
        var context = await CreateContextAsync("Theater_TestData_4.json");
        using var rendered = await service.GetCardAsync(context);
        Assert.That(rendered.Length, Is.GreaterThan(0));

        provider.Dispose();
        var exception = Assert.ThrowsAsync<CommandException>(async () =>
        {
            using var ignored = await service.GetCardAsync(context);
        });
        Assert.That(exception!.InnerException, Is.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task GetCardAsync_InvalidDifficulty_PreservesValidationFailure()
    {
        var json = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(TestDataPath, "Genshin", "Theater_TestData_4.json")))!;
        json["stat"]!["difficulty_id"] = 0;
        var theaterData = json.Deserialize<GenshinTheaterInformation>()!;
        var context = CreateContext(theaterData);

        var exception = Assert.ThrowsAsync<CommandException>(async () => await m_Service.GetCardAsync(context));

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Difficulty must be between 1 and 5"));
    }

    [Test]
    public async Task GetCardAsync_ConcurrentRequests_ReturnUnchangedIndependentResults()
    {
        var context = await CreateContextAsync("Theater_TestData_4.json");
        var streams = await Task.WhenAll(
            m_Service.GetCardAsync(context),
            m_Service.GetCardAsync(context));
        try
        {
            Assert.That(streams[0], Is.Not.SameAs(streams[1]));
            Assert.That(streams[0], IsImage.PerceptuallyEquivalentTo(streams[1]));
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    [Test]
    public async Task Dispose_ReleasesPreparedVariantsWithoutInvalidatingReturnedClone()
    {
        var context = await CreateContextAsync("Theater_TestData_4.json");
        using var background = m_Service.CreateBackgroundForTest(context);

        m_Service.Dispose();

        Assert.DoesNotThrow(() => background.Mutate(ctx => ctx.Brightness(0.9f)));
        Assert.That(() => m_Service.CreateBackgroundForTest(context),
            Throws.TypeOf<ObjectDisposedException>());
    }

    private static async Task<BaseCardGenerationContext<GenshinTheaterInformation>> CreateContextAsync(
        string testDataFileName)
    {
        await using var stream = File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName));
        var theaterData = await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(stream);
        return CreateContext(theaterData!);
    }

    private static BaseCardGenerationContext<GenshinTheaterInformation> CreateContext(
        GenshinTheaterInformation theaterData)
    {
        var context = new BaseCardGenerationContext<GenshinTheaterInformation>(
            TestUserId, theaterData, GetTestUserGameData());
        context.SetParameter("server", Server.Asia);
        context.SetParameter("constMap", GetTestConstDictionary());
        return context;
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

    private sealed class TestableGenshinTheaterCardService(
        IImageRepository imageRepository,
        ILogger<GenshinTheaterCardService> logger,
        IApplicationMetrics metrics)
        : GenshinTheaterCardService(imageRepository, logger, metrics)
    {
        public Image<Rgba32> CreateBackgroundForTest(
            ICardGenerationContext<GenshinTheaterInformation> context) => base.CreateBackground(context);

        public Image<Rgba32> CreateLegacyBackgroundForTest(int difficulty)
        {
            var maxRound = difficulty switch
            {
                1 => 3,
                2 => 6,
                3 => 8,
                4 => 10,
                5 => 12,
                _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
            };
            var height = 620 + (maxRound + 1) / 2 * 300;
            var background = StaticBackground!.CloneAs<Rgba32>();
            if (height > background.Height)
                background.Mutate(ctx => ctx.Resize(0, height));
            var rectangle = new Rectangle(background.Width / 2 - 1900 / 2,
                background.Height / 2 - height / 2, 1900, height);
            background.Mutate(ctx =>
            {
                ctx.Crop(rectangle);
                ctx.GaussianBlur(10);
            });
            return background;
        }
    }

    [Explicit]
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
    [TestCase("Theater_TestData_10.json")]
    public async Task GenerateGoldenImage(string filename)
    {
        var testData = await
            JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
                File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
            "Genshin", filename)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<GenshinTheaterInformation>(TestUserId, testData!, userGameData);
        cardContext.SetParameter("server", Server.Asia);
        cardContext.SetParameter("constMap", GetTestConstDictionary());

        var stream = await m_Service.GetCardAsync(cardContext);
        var fs = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
               "TestAssets", filename.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));
        await stream.CopyToAsync(fs);
        await fs.FlushAsync();
        await fs.DisposeAsync();
    }

}
