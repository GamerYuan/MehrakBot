#region

using System.Text.Json;
using Mehrak.GameApi.Zzz.Types;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Zzz;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzAssaultCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ImageRepository m_ImageRepository;
    private ZzzAssaultCardService m_Service;
    private readonly Dictionary<string, Stream> m_BossImage = [];
    private readonly Dictionary<string, Stream> m_BuffImage = [];

    [SetUp]
    public async Task Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, new NullLogger<ImageRepository>());

        m_Service = new ZzzAssaultCardService(m_ImageRepository,
            NullLogger<ZzzAssaultCardService>.Instance);
        await m_Service.InitializeAsync();

        m_BossImage.Add("Notorious - Dead End Butcher",
            await m_ImageRepository.DownloadFileToStreamAsync("Notorious_DeadEndButcher"));
        m_BossImage.Add("Miasma Priest",
            await m_ImageRepository.DownloadFileToStreamAsync("MiasmaPriest"));
        m_BossImage.Add("Autonomous Assault Unit - Typhon Destroyer",
            await m_ImageRepository.DownloadFileToStreamAsync("AutonomousAssaultUnit_TyphonDestroyer"));

        m_BuffImage.Add("Lingering Illness",
            await m_ImageRepository.DownloadFileToStreamAsync("Buff_LingeringIllness"));
        m_BuffImage.Add("Sonata",
            await m_ImageRepository.DownloadFileToStreamAsync("Buff_Sonata"));
        m_BuffImage.Add("Power Boost",
            await m_ImageRepository.DownloadFileToStreamAsync("Buff_PowerBoost"));
    }

    [TearDown]
    public void TearDown()
    {
        m_BuffImage.Clear();
        m_BossImage.Clear();
    }

    [Test]
    [TestCase("Da_TestData_1.json")]
    [TestCase("Da_TestData_2.json")]
    public async Task GetAssaultCardAsync_TestData_ShouldMatchGoldenImage(string testData)
    {
        ZzzAssaultData? assaultData = JsonSerializer.Deserialize<ZzzAssaultData>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(assaultData, Is.Not.Null);

        byte[] goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets",
            $"{Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage")}.jpg"));
        Stream image = await m_Service.GetAssaultCardAsync(assaultData,
            GetUserGameData(), m_BossImage, m_BuffImage);
        Assert.That(image, Is.Not.Null);
        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        byte[] generatedImageBytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"ZzzAssault_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);
        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"ZzzAssault_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImage), "Generated image should match the golden image");
    }

    private static UserGameData GetUserGameData()
    {
        return new()
        {
            GameUid = "1300000000",
            GameBiz = "nap_global",
            Nickname = "Test",
            Region = "prod_gf_jp",
            Level = 60
        };
    }

    /*
    [Test]
    [TestCase("Da_TestData_1.json")]
    [TestCase("Da_TestData_2.json")]
    public async Task GenerateGoldenImage(string testData)
    {
        ZzzAssaultData? assaultData = JsonSerializer.Deserialize<ZzzAssaultData>(
                    await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(assaultData, Is.Not.Null);

        Stream image = await m_Service.GetAssaultCardAsync(assaultData,
            GetUserGameData(), m_BossImage, m_BuffImage);
        FileStream fileStream = File.OpenWrite(
            $"{Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
                Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage"))}.jpg");

        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
    }

    [Test]
    [TestCase("Da_TestData_1.json")]
    public async Task GetBossAndBuffImage(string testData)
    {
        Mock<IHttpClientFactory> httpClientFactoryMock = new();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        ZzzAssaultApiService apiService = new(httpClientFactoryMock.Object,
            NullLogger<ZzzAssaultApiService>.Instance);

        ZzzAssaultData? assaultData = JsonSerializer.Deserialize<ZzzAssaultData>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(assaultData, Is.Not.Null);

        foreach (AssaultBoss? boss in assaultData.List.SelectMany(x => x.Boss))
        {
            Stream stream = new MemoryStream();
            await apiService.GetBossImageAsync(boss, stream);
            FileStream fileStream = File.OpenWrite(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets", $"{boss.Name.Replace(" - ", "_").Replace(" ", "")}.png"));
            await stream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }

        foreach (AssaultBuff? buff in assaultData.List.SelectMany(x => x.Buff).Where(x => x is not null).DistinctBy(x => x!.Name))
        {
            Stream stream = new MemoryStream();
            await apiService.GetBuffImageAsync(buff!.Icon, stream);
            FileStream fileStream = File.OpenWrite(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets", $"Buff_{buff.Name.Replace(" ", "")}.png"));
            await stream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }
    }
    */
}