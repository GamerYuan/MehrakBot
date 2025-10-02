#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Genshin.Theater;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinTheaterCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private GenshinTheaterCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new GenshinTheaterCardService(m_ImageRepository, NullLogger<GenshinTheaterCardService>.Instance);
        await m_Service.InitializeAsync();
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
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        GenshinTheaterInformation? testData =
            await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        UserGameData userGameData = GetTestUserGameData();

        Stream stream =
            await m_Service.GetTheaterCardAsync(testData!, userGameData, GetTestConstDictionary(), GetBuffImages());
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"GenshinTheater_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinTheater_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    private static UserGameData GetTestUserGameData()
    {
        return new UserGameData
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 60,
            Region = "os_asia",
            RegionName = "Asia"
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

    private Dictionary<string, Stream> GetBuffImages()
    {
        return new Dictionary<string, Stream>
        {
            {
                "Cryo Crystal Blessing",
                m_ImageRepository.DownloadFileToStreamAsync("Test_CryoCrystalBlessing").Result
            },
            {
                "Frozen Blessing", m_ImageRepository.DownloadFileToStreamAsync("Test_FrozenBlessing").Result
            },
            {
                "Hydro Crystal Blessing",
                m_ImageRepository.DownloadFileToStreamAsync("Test_HydroCrystalBlessing").Result
            }
        };
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
    public async Task GenerateGoldenImage(string testData)
    {
        GenshinTheaterInformation? testData1 = await
            JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
            "Genshin", testData)));
        Assert.That(testData1, Is.Not.Null, "Test data should not be null");

        UserGameData userGameData = GetTestUserGameData();
        Stream stream = await m_Service.GetTheaterCardAsync(testData1!, userGameData, GetTestConstDictionary(), GetBuffImages());
        FileStream fs = File.OpenWrite(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testData.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));
        await stream.CopyToAsync(fs);
        await fs.FlushAsync();
    }
}
