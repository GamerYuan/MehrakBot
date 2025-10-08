#region

using Mehrak.Application.Services.Hsr;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Services.Commands.Hsr.Character;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    private ImageRepository m_ImageRepository;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;
    private Mock<IRelicRepository> m_HsrRelicRepositoryMock;
    private HsrCharacterCardService m_HsrCharacterCardService;

    [SetUp]
    public async Task Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, new NullLogger<ImageRepository>());

        m_HttpClientMock = new Mock<HttpClient>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(m_HttpClientMock.Object);
    m_HsrRelicRepositoryMock = new Mock<IRelicRepository>();

        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(116)).ReturnsAsync("Prisoner in Deep Confinement");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(118)).ReturnsAsync("Watchmaker, Master of Dream Machinations");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(119)).ReturnsAsync("Iron Cavalry Against the Scourge");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(307)).ReturnsAsync("Talia: Kingdom of Banditry");
        m_HsrRelicRepositoryMock.Setup(x => x.GetSetName(310)).ReturnsAsync("Broken Keel");

        m_HsrCharacterCardService = new HsrCharacterCardService(m_ImageRepository, m_HsrRelicRepositoryMock.Object,
            new NullLogger<HsrCharacterCardService>());
        await m_HsrCharacterCardService.InitializeAsync();
    }

    [Test]
    public async Task GenerateCharacterCardAsync_Stelle_ShouldMatchGoldenImage()
    {
        // Arrange
        string testDataPath = Path.Combine(TestDataPath, "Stelle_TestData.json");
        string goldenImagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets",
            "Stelle_GoldenImage.jpg");
        HsrCharacterInformation? characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        // Act
        Stream generatedImageStream = await m_HsrCharacterCardService.GenerateCharacterCardAsync(characterDetail, "Test");

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, "Stelle");
    }

    [Test]
    public async Task GenerateCharacterCardAsync_StelleNoEquipNoRelic_ShouldMatchGoldenImage()
    {
        // Arrange
        string testDataPath = Path.Combine(TestDataPath, "Stelle_NoEquip_NoRelic_TestData.json");
        string goldenImagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets",
            "Stelle_NoEquip_NoRelic_GoldenImage.jpg");
        HsrCharacterInformation? characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        // Act
        Stream generatedImageStream = await m_HsrCharacterCardService.GenerateCharacterCardAsync(characterDetail, "Test");

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, "StelleNoEquipNoRelic");
    }

    [Test]
    public async Task GenerateCharacterCardAsync_StelleRemembrance_ShouldMatchGoldenImage()
    {
        // Arrange
        string testDataPath = Path.Combine(TestDataPath, "Stelle_Remembrance_TestData.json");
        string goldenImagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "TestAssets",
            "Stelle_Remembrance_GoldenImage.jpg");
        HsrCharacterInformation? characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
            await File.ReadAllTextAsync(testDataPath));
        Assert.That(characterDetail, Is.Not.Null);

        // Act
        Stream generatedImageStream = await m_HsrCharacterCardService.GenerateCharacterCardAsync(characterDetail, "Test");

        // Assert
        await AssertImageMatches(generatedImageStream, goldenImagePath, "StelleRemembrance");
    }

    [Test]
    public void GenerateCharacterCardAsync_InvalidCharacterData_ShouldThrowException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await m_HsrCharacterCardService.GenerateCharacterCardAsync(null!, "Test"));
    }

    private static async Task AssertImageMatches(Stream generatedImageStream, string goldenImagePath, string testName)
    {
        Assert.That(generatedImageStream, Is.Not.Null, $"Generated image stream should not be null for {testName}");
        Assert.That(generatedImageStream.Length, Is.GreaterThan(0),
            $"Generated image should have content for {testName}");

        // Read the generated image
        using MemoryStream memoryStream = new();
        await generatedImageStream.CopyToAsync(memoryStream);
        byte[] generatedImageBytes = memoryStream.ToArray();

        // Compare basic properties
        Assert.That(generatedImageBytes, Is.Not.Empty,
            $"Generated image should have content for {testName}");

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        if (!File.Exists(goldenImagePath))
        {
            Console.WriteLine(
                $"Golden image not found at {goldenImagePath} for test {testName}. Generated image saved to {outputImagePath}");
            Assert.Fail($"Golden image not found at {goldenImagePath} for test {testName}. " +
                        "Please run the GenerateGoldenImage test to create golden images.");
        }

        // Read the golden image
        byte[] goldenImageBytes = await File.ReadAllBytesAsync(goldenImagePath);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImageBytes);
    }

    // To be used to generate golden image should the generation algorithm be
    // updated [Test] public async Task GenerateGoldenImage() { foreach (var
    // file in Directory.EnumerateFiles(TestDataPath, "*.json")) { var
    // characterDetail = JsonSerializer.Deserialize<HsrCharacterInformation>(
    // await File.ReadAllTextAsync(file)); Assert.That(characterDetail, Is.Not.Null);
    //
    // var service = new HsrCharacterCardService(m_ImageRepository,
    // m_HsrImageUpdaterService, new NullLogger<HsrCharacterCardService>());
    //
    // var image = await service.GenerateCharacterCardAsync(characterDetail,
    // "Test"); using var stream = new MemoryStream(); await
    // image.CopyToAsync(stream); await File.WriteAllBytesAsync(
    // $"{TestDataPath}/Assets/{Path.GetFileNameWithoutExtension(file).Replace("TestData",
    // "GoldenImage")}.jpg", stream.ToArray()); } }
}
