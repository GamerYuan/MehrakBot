#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Genshin.Stygian;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinStygianCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private GenshinStygianCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");


    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new GenshinStygianCardService(m_ImageRepository, NullLogger<GenshinStygianCardService>.Instance);
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

    private Dictionary<int, Stream> GetMonsterImages()
    {
        return new Dictionary<int, Stream>
        {
            {
                1001061,
                m_ImageRepository.DownloadFileToStreamAsync("Test_HydroTulpa").Result
            },
            {
                1001062, m_ImageRepository.DownloadFileToStreamAsync("Test_LavaDragon").Result
            },
            {
                1001063,
                m_ImageRepository.DownloadFileToStreamAsync("Test_SecretSourceAutomaton").Result
            }
        };
    }

    [Test]
    public async Task GenerateGoldenImage()
    {
        var testData1 = await JsonSerializer.DeserializeAsync<StygianData>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Stygian_TestData_1.json")));
        var testData2 = await JsonSerializer.DeserializeAsync<StygianData>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Stygian_TestData_2.json")));
        var testData3 = await JsonSerializer.DeserializeAsync<StygianData>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
                "Stygian_TestData_3.json")));

        var image1 =
            await m_Service.GetStygianCardImageAsync(testData1!, GetTestUserGameData(), Regions.Asia,
                GetMonsterImages());
        var image2 =
            await m_Service.GetStygianCardImageAsync(testData2!, GetTestUserGameData(), Regions.Asia,
                GetMonsterImages());
        var image3 =
            await m_Service.GetStygianCardImageAsync(testData3!, GetTestUserGameData(), Regions.Asia,
                GetMonsterImages());

        Assert.Multiple(() =>
        {
            Assert.That(image1, Is.Not.Null);
            Assert.That(image2, Is.Not.Null);
            Assert.That(image3, Is.Not.Null);
        });

        await using var fileStream1 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Stygian_GoldenImage_1.jpg"));
        await using var fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Stygian_GoldenImage_2.jpg"));
        await using var fileStream3 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", "Stygian_GoldenImage_3.jpg"));

        await image1.CopyToAsync(fileStream1);
        await image2.CopyToAsync(fileStream2);
        await image3.CopyToAsync(fileStream3);
    }
}
