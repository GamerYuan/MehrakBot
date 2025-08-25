using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Zzz;
using MehrakCore.Services.Commands.Zzz.Character;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace MehrakCore.Tests.Services.Commands.Zzz;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ImageRepository m_ImageRepository;
    private ZzzImageUpdaterService m_ZzzImageUpdaterService;
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

        m_ZzzImageUpdaterService = new ZzzImageUpdaterService(m_ImageRepository, m_HttpClientFactoryMock.Object,
            new NullLogger<ZzzImageUpdaterService>());
    }

    [Test]
    [TestCase("Jane_TestData.json")]
    [TestCase("Miyabi_TestData.json")]
    public async Task GenerateCharacterCardAsync_TestData_ShouldMatchGoldenImage(string testData)
    {
        ZzzFullAvatarData? characterDetail =
                JsonSerializer.Deserialize<ZzzFullAvatarData>(
                    await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(characterDetail, Is.Not.Null);

        ZzzCharacterCardService service = new(m_ImageRepository,
            NullLogger<ZzzCharacterCardService>.Instance);
        byte[] goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
            $"{Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage")}.jpg"));

        Stream image = await service.GenerateCharacterCardAsync(characterDetail, "Test");
        Assert.That(image, Is.Not.Null);
        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);

        memoryStream.Position = 0;

        byte[] generatedImageBytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(testData)}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);
        string outputGoldenImagePath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(testData)}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImage));
    }

    // To be used to generate golden image should the generation algorithm be updated
    [Test]
    [TestCase("Jane_TestData.json")]
    [TestCase("Miyabi_TestData.json")]
    public async Task GenerateGoldenImage(string testData)
    {
        ZzzFullAvatarData? characterDetail =
                JsonSerializer.Deserialize<ZzzFullAvatarData>(
                    await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(characterDetail, Is.Not.Null);

        ZzzCharacterCardService service = new(m_ImageRepository,
            NullLogger<ZzzCharacterCardService>.Instance);

        Stream image = await service.GenerateCharacterCardAsync(characterDetail, "Test");
        FileStream fileStream = File.OpenWrite(
            $"{Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
                Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage"))}.jpg");

        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
    }
}
