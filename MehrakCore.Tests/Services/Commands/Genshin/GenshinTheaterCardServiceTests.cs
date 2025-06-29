#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Genshin.Theater;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

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
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new GenshinTheaterCardService(m_ImageRepository, NullLogger<GenshinTheaterCardService>.Instance);
    }

    [Test]
    public async Task GetAbyssCardAsync_Data1_MatchesGoldenImage()
    {
        var testData =
            await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", "Theater_TestData_1.json")));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", "Theater_GoldenImage_1.jpg"));

        var userGameData = GetTestUserGameData();

        var stream =
            await m_Service.GetTheaterCardAsync(testData!, userGameData, GetTestConstDictionary(), GetBuffImages());
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory, "GenshinTheater_Data1_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, "GenshinTheater_Data1_Golden.jpg");
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
    public async Task GenerateGoldenImage()
    {
        var testData1 = await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Theater_TestData_1.json")));
        var testData2 = await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Theater_TestData_2.json")));
        var testData3 = await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Theater_TestData_3.json")));
        var testData4 = await JsonSerializer.DeserializeAsync<GenshinTheaterInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Theater_TestData_4.json")));

        var image1 =
            await m_Service.GetTheaterCardAsync(testData1!, GetTestUserGameData(), GetTestConstDictionary(),
                GetBuffImages());
        var image2 =
            await m_Service.GetTheaterCardAsync(testData2!, GetTestUserGameData(), GetTestConstDictionary(),
                GetBuffImages());
        var image3 =
            await m_Service.GetTheaterCardAsync(testData3!, GetTestUserGameData(), GetTestConstDictionary(),
                GetBuffImages());
        var image4 =
            await m_Service.GetTheaterCardAsync(testData4!, GetTestUserGameData(), GetTestConstDictionary(),
                GetBuffImages());

        Assert.Multiple(() =>
        {
            Assert.That(image1, Is.Not.Null);
            Assert.That(image2, Is.Not.Null);
            Assert.That(image3, Is.Not.Null);
            Assert.That(image4, Is.Not.Null);
        });

        await using var fileStream1 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Theater_GoldenImage_1.jpg"));
        await using var fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Theater_GoldenImage_2.jpg"));
        await using var fileStream3 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Theater_GoldenImage_3.jpg"));
        await using var fileStream4 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Theater_GoldenImage_4.jpg"));

        await image1.CopyToAsync(fileStream1);
        await image2.CopyToAsync(fileStream2);
        await image3.CopyToAsync(fileStream3);
        await image4.CopyToAsync(fileStream4);
    }
}
