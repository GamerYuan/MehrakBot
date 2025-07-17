#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Genshin.Abyss;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinAbyssCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private GenshinAbyssCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new GenshinAbyssCardService(m_ImageRepository, NullLogger<GenshinAbyssCardService>.Instance);
    }

    [Test]
    [TestCase("Abyss_TestData_1.json")]
    [TestCase("Abyss_TestData_2.json")]
    [TestCase("Abyss_TestData_3.json")]
    public async Task GetAbyssCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<GenshinAbyssInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var stream =
            await m_Service.GetAbyssCardAsync(12, userGameData, Regions.Asia, testData!, GetTestConstDictionary());
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"GenshinAbyss_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinAbyss_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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
            { 10000063, 2 },
            { 10000089, 6 },
            { 10000103, 3 },
            { 10000106, 0 },
            { 10000107, 4 },
            { 10000112, 5 }
        };
    }

    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     var testData1 = await JsonSerializer.DeserializeAsync<GenshinAbyssInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //             "Abyss_TestData_1.json")));
    //     var testData2 = await JsonSerializer.DeserializeAsync<GenshinAbyssInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //             "Abyss_TestData_2.json")));
    //     var testData3 = await JsonSerializer.DeserializeAsync<GenshinAbyssInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //             "Abyss_TestData_3.json")));
    //
    //     var image1 = await m_Service.GetAbyssCardAsync(12, GetTestUserGameData(), testData1!, GetTestConstDictionary());
    //     var image2 = await m_Service.GetAbyssCardAsync(12, GetTestUserGameData(), testData2!, GetTestConstDictionary());
    //     var image3 = await m_Service.GetAbyssCardAsync(12, GetTestUserGameData(), testData3!, GetTestConstDictionary());
    //
    //     Assert.That(image1, Is.Not.Null);
    //     Assert.That(image2, Is.Not.Null);
    //     Assert.That(image3, Is.Not.Null);
    //
    //     await using var fileStream1 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "Abyss_GoldenImage_1.jpg"));
    //     await using var fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "Abyss_GoldenImage_2.jpg"));
    //     await using var fileStream3 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "Abyss_GoldenImage_3.jpg"));
    //
    //     await image1.CopyToAsync(fileStream1);
    //     await image2.CopyToAsync(fileStream2);
    //     await image3.CopyToAsync(fileStream3);
    // }
}
