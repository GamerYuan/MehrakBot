#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Application.Zzz.Defense;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Tests.Zzz.Defense;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzDefenseCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private TestableZzzDefenseCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "1300000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = await CreateServiceAsync();
    }

    [TearDown]
    public void TearDown()
    {
        m_Service.Dispose();
    }

    [Test]
    [TestCase("Shiyu_TestData_1.json")]
    [TestCase("Shiyu_TestData_2.json")]
    [TestCase("Shiyu_TestData_3.json")]
    [TestCase("Shiyu_TestData_4.json")]
    [TestCase("Shiyu_TestData_5.json")]
    public async Task GetDefenseCardAsync_TestData_ShouldMatchGoldenImage(string testData)
    {
        var defenseData = JsonSerializer.Deserialize<ZzzDefenseDataV2>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(defenseData, Is.Not.Null);

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets",
            $"{Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage")}.jpg"));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<ZzzDefenseDataV2>(TestUserId, defenseData, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        var image = await m_Service.GetCardAsync(cardContext);
        Assert.That(image, Is.Not.Null);

        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        var generatedImageBytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"ZzzDefense_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"ZzzDefense_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(generatedImageBytes, Is.Not.Empty);
        using var goldenStream = new MemoryStream(goldenImage);
        Assert.That(memoryStream, IsImage.IdenticalTo(goldenStream), "Generated image should match the golden image");
    }

    [Test]
    public async Task PreparedBackground_MatchesLegacyTransformAndReturnsIndependentClones()
    {
        await using var backgroundStream = await S3TestHelper.Instance.ImageRepository
            .DownloadFileToStreamAsync(FileNameFormat.Zzz.ShiyuBackgroundName);
        using var expected = await Image.LoadAsync<Rgba32>(backgroundStream);
        expected.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            CenterCoordinates = new PointF(ctx.GetCurrentSize().Width / 2f, ctx.GetCurrentSize().Height / 2f),
            Size = new Size(1000, 1080),
            Mode = ResizeMode.Crop,
            Sampler = KnownResamplers.Bicubic
        }));

        var cloneTasks = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(m_Service.CreateBackgroundForTest))
            .ToArray();
        var clones = await Task.WhenAll(cloneTasks);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(clones, Has.All.Property(nameof(Image.Width)).EqualTo(1000));
                Assert.That(clones, Has.All.Property(nameof(Image.Height)).EqualTo(1080));
            });

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
    public async Task BenchmarkLegacyBackground_MatchesPreparedBackground()
    {
        using var expected = m_Service.CreateBackgroundForTest();
        using var scope = CardBenchmarkMetrics.BeginScope(
            "defense-legacy-background-equivalence",
            useLegacyBackgrounds: true);
        using var legacyService = await CreateServiceAsync();
        using var actual = legacyService.CreateScopedBackgroundForTest();

        using var expectedStream = new MemoryStream();
        using var actualStream = new MemoryStream();
        await expected.SaveAsPngAsync(expectedStream);
        await actual.SaveAsPngAsync(actualStream);
        Assert.That(actualStream, IsImage.PerceptuallyEquivalentTo(expectedStream));
    }

    [Test]
    public async Task GetCardAsync_ConcurrentRequests_ReturnUnchangedIndependentResults()
    {
        var defenseData = JsonSerializer.Deserialize<ZzzDefenseDataV2>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, "Shiyu_TestData_1.json")))!;
        var context = new BaseCardGenerationContext<ZzzDefenseDataV2>(
            TestUserId, defenseData, GetTestUserGameData());
        context.SetParameter("server", Server.Asia);

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

    private static async Task<TestableZzzDefenseCardService> CreateServiceAsync()
    {
        var service = new TestableZzzDefenseCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzDefenseCardService>>(),
            CardBenchmarkMetrics.Create());
        try
        {
            await service.InitializeAsync();
            if (CardBenchmarkMetrics.IsBenchmarkScope)
                await service.LoadBenchmarkOriginalBackgroundAsync();
            return service;
        }
        catch
        {
            service.Dispose();
            throw;
        }
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

    private sealed class TestableZzzDefenseCardService(
        IImageRepository imageRepository,
        ILogger<ZzzDefenseCardService> logger,
        IApplicationMetrics metrics)
        : ZzzDefenseCardService(imageRepository, logger, metrics), IDisposable
    {
        private Image<Rgba32>? m_BenchmarkOriginalBackground;

        public async Task LoadBenchmarkOriginalBackgroundAsync()
        {
            await using var stream = await S3TestHelper.Instance.ImageRepository
                .DownloadFileToStreamAsync(FileNameFormat.Zzz.ShiyuBackgroundName);
            var background = await Image.LoadAsync<Rgba32>(stream);
            m_BenchmarkOriginalBackground?.Dispose();
            m_BenchmarkOriginalBackground = background;
        }

        protected override Image<Rgba32> CreateBackground()
        {
            if (!CardBenchmarkMetrics.UseLegacyBackgrounds)
                return base.CreateBackground();

            var source = m_BenchmarkOriginalBackground
                         ?? throw new InvalidOperationException(
                             "The original Defense background was not loaded for the benchmark scope");
            var background = source.CloneAs<Rgba32>();
            try
            {
                background.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    CenterCoordinates = new PointF(
                        ctx.GetCurrentSize().Width / 2f,
                        ctx.GetCurrentSize().Height / 2f),
                    Size = new Size(1000, 1080),
                    Mode = ResizeMode.Crop,
                    Sampler = KnownResamplers.Bicubic
                }));
                return background;
            }
            catch
            {
                background.Dispose();
                throw;
            }
        }

        public Image<Rgba32> CreateBackgroundForTest() => base.CreateBackground();

        public Image<Rgba32> CreateScopedBackgroundForTest() => CreateBackground();

        public void Dispose()
        {
            m_BenchmarkOriginalBackground?.Dispose();
            m_BenchmarkOriginalBackground = null;
        }
    }

    [Explicit]
    [Test]
    [TestCase("Shiyu_TestData_1.json", "Shiyu_GoldenImage_1.jpg")]
    [TestCase("Shiyu_TestData_2.json", "Shiyu_GoldenImage_2.jpg")]
    [TestCase("Shiyu_TestData_3.json", "Shiyu_GoldenImage_3.jpg")]
    [TestCase("Shiyu_TestData_4.json", "Shiyu_GoldenImage_4.jpg")]
    [TestCase("Shiyu_TestData_5.json", "Shiyu_GoldenImage_5.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        var defenseData =
            JsonSerializer.Deserialize<ZzzDefenseDataV2>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(defenseData, Is.Not.Null);

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<ZzzDefenseDataV2>(TestUserId, defenseData, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        var image = await m_Service.GetCardAsync(cardContext);

        using var fileStream = File.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
                goldenImageFileName));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();

        Assert.That(image, Is.Not.Null);
    }
}
