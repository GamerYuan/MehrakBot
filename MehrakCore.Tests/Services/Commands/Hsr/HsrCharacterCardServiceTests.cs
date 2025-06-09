#region

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

public class HsrCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    private MongoTestHelper m_MongoTestHelper;
    private ImageRepository m_ImageRepository;
    private HsrImageUpdaterService m_HsrImageUpdaterService;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;

    [SetUp]
    public async Task Setup()
    {
        m_MongoTestHelper = new MongoTestHelper();

        m_ImageRepository = new ImageRepository(m_MongoTestHelper.MongoDbService, new NullLogger<ImageRepository>());

        m_HttpClientMock = new Mock<HttpClient>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(m_HttpClientMock.Object);

        m_HsrImageUpdaterService = new HsrImageUpdaterService(m_ImageRepository, m_HttpClientFactoryMock.Object,
            new NullLogger<HsrImageUpdaterService>());
        foreach (var image in
                 Directory.EnumerateFiles(Path.Combine(TestDataPath, "Assets"), "*.png").Concat(
                     Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr"), "*.png")))
        {
            var fileName = Path.GetFileNameWithoutExtension(image);
            if (await m_ImageRepository.FileExistsAsync(fileName)) continue;

            await using var stream = File.OpenRead(image);
            await m_ImageRepository.UploadFileAsync(fileName, stream);
        }

        var dict =
            (ConcurrentDictionary<int, string>)typeof(HsrImageUpdaterService).GetField("m_SetMapping",
                BindingFlags.NonPublic |
                BindingFlags.Instance)!.GetValue(m_HsrImageUpdaterService)!;
        dict.TryAdd(61161, "Prisoner in Deep Confinement");
        dict.TryAdd(61162, "Prisoner in Deep Confinement");
        dict.TryAdd(61163, "Prisoner in Deep Confinement");
        dict.TryAdd(61164, "Prisoner in Deep Confinement");
        dict.TryAdd(61192, "Iron Cavalry Against the Scourge");
        dict.TryAdd(61183, "Watchmaker, Master of Dream Machinations");
        dict.TryAdd(63075, "Talia: Kingdom of Banditry");
        dict.TryAdd(63076, "Talia: Kingdom of Banditry");
        dict.TryAdd(63105, "Broken Keel");
        dict.TryAdd(63106, "Broken Keel");
    }

    [TearDown]
    public void TearDown()
    {
        m_MongoTestHelper.Dispose();
    }

    [Test]
    public async Task GenerateCharacterCardAsync_Stelle_ShouldMatchGoldenImage()
    {
        // Arrange
        var testDataPath = Path.Combine(TestDataPath, "Stelle_TestData.json");
        var goldenImagePath = Path.Combine(TestDataPath, "Assets", "Stelle_GoldenImage.jpg");
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var service = new HsrCharacterCardService(m_ImageRepository, m_HsrImageUpdaterService,
            new NullLogger<HsrCharacterCardService>());

        // Act
        var generatedImageStream = await service.GenerateCharacterCardAsync(characterDetail, "Test");

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, "Stelle");
    }

    [Test]
    public async Task GenerateCharacterCardAsync_StelleNoEquipNoRelic_ShouldMatchGoldenImage()
    {
        // Arrange
        var testDataPath = Path.Combine(TestDataPath, "Stelle_NoEquip_NoRelic_TestData.json");
        var goldenImagePath = Path.Combine(TestDataPath, "Assets", "Stelle_NoEquip_NoRelic_GoldenImage.jpg");
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var service = new HsrCharacterCardService(m_ImageRepository, m_HsrImageUpdaterService,
            new NullLogger<HsrCharacterCardService>());

        // Act
        var generatedImageStream = await service.GenerateCharacterCardAsync(characterDetail, "Test");

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, "StelleNoEquipNoRelic");
    }

    [Test]
    public async Task GenerateCharacterCardAsync_StelleRemembrance_ShouldMatchGoldenImage()
    {
        // Arrange
        var testDataPath = Path.Combine(TestDataPath, "Stelle_Remembrance_TestData.json");
        var goldenImagePath = Path.Combine(TestDataPath, "Assets", "Stelle_Remembrance_GoldenImage.jpg");
        var characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        var service = new HsrCharacterCardService(m_ImageRepository, m_HsrImageUpdaterService,
            new NullLogger<HsrCharacterCardService>());

        // Act
        var generatedImageStream = await service.GenerateCharacterCardAsync(characterDetail, "Test");

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, "StelleRemembrance");
    }

    [Test]
    public void GenerateCharacterCardAsync_InvalidCharacterData_ShouldThrowException()
    {
        // Arrange
        var service = new HsrCharacterCardService(m_ImageRepository, m_HsrImageUpdaterService,
            new NullLogger<HsrCharacterCardService>());

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.GenerateCharacterCardAsync(null!, "Test"));
    }

    private static async Task AssertImageMatches(Stream generatedImageStream, string goldenImagePath, string testName)
    {
        Assert.That(generatedImageStream, Is.Not.Null, $"Generated image stream should not be null for {testName}");
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0),
            $"Generated image should have content for {testName}");

        if (!File.Exists(goldenImagePath))
            Assert.Fail($"Golden image not found at {goldenImagePath} for test {testName}. " +
                        "Please run the GenerateGoldenImage test to create golden images.");

        // Read the golden image
        var goldenImageBytes = await File.ReadAllBytesAsync(goldenImagePath);

        // Read the generated image
        using var memoryStream = new MemoryStream();
        await generatedImageStream.CopyToAsync(memoryStream);
        var generatedImageBytes = memoryStream.ToArray();

        // Compare basic properties
        Assert.That(generatedImageBytes.Length, Is.GreaterThan(0),
            $"Generated image should have content for {testName}");

        // For more robust comparison, we can use image similarity comparison
        // This is a basic comparison - in practice you might want to use a more sophisticated image comparison
        using var goldenImage = Image.Load(goldenImageBytes);
        using var generatedImage = Image.Load(generatedImageBytes);

        Assert.Multiple(() =>
        {
            Assert.That(generatedImage.Width, Is.EqualTo(goldenImage.Width),
                $"Generated image width should match golden image width for {testName}");
            Assert.That(generatedImage.Height, Is.EqualTo(goldenImage.Height),
                $"Generated image height should match golden image height for {testName}");
        });
    }

    // To be used to generate golden image should the generation algorithm be updated
    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     foreach (var file in Directory.EnumerateFiles(TestDataPath, "*.json"))
    //     {
    //         var characterDetail =
    //             JsonSerializer.Deserialize<HsrCharacterInformation>(
    //                 await File.ReadAllTextAsync(file));
    //         Assert.That(characterDetail, Is.Not.Null);
    //
    //         var service = new HsrCharacterCardService(m_ImageRepository, m_HsrImageUpdaterService,
    //             new NullLogger<HsrCharacterCardService>());
    //
    //         var image = await service.GenerateCharacterCardAsync(characterDetail, "Test");
    //         using var stream = new MemoryStream();
    //         await image.CopyToAsync(stream);
    //         await File.WriteAllBytesAsync(
    //             $"{TestDataPath}/Assets/{Path.GetFileNameWithoutExtension(file).Replace("TestData", "GoldenImage")}.jpg",
    //             stream.ToArray());
    //     }
    // }
}
