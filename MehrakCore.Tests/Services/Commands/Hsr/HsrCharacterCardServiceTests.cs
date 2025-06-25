#region

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr;
using MehrakCore.Services.Commands.Hsr.Character;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    private ImageRepository m_ImageRepository;
    private HsrImageUpdaterService m_HsrImageUpdaterService;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;

    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, new NullLogger<ImageRepository>());

        m_HttpClientMock = new Mock<HttpClient>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(m_HttpClientMock.Object);

        m_HsrImageUpdaterService = new HsrImageUpdaterService(m_ImageRepository, m_HttpClientFactoryMock.Object,
            new NullLogger<HsrImageUpdaterService>());

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

        // Read the generated image
        using var memoryStream = new MemoryStream();
        await generatedImageStream.CopyToAsync(memoryStream);
        var generatedImageBytes = memoryStream.ToArray();

        // Compare basic properties
        Assert.That(generatedImageBytes, Is.Not.Empty,
            $"Generated image should have content for {testName}");

        // Save generated image to output folder for comparison
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

        // Read the golden image
        var goldenImageBytes = await File.ReadAllBytesAsync(goldenImagePath);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImageBytes);
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
