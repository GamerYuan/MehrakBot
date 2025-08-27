#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr.EndGame;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrEndGameCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private HsrEndGameCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [SetUp]
    public async Task Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new HsrEndGameCardService(m_ImageRepository, NullLogger<HsrEndGameCardService>.Instance);
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Pf_TestData_1.json")]
    [TestCase("Pf_TestData_2.json")]
    [TestCase("Pf_TestData_3.json")]
    public async Task GetEndGameCardAsync_PureFictionTestData_MatchesGoldenImage(string testDataFileName)
    {
        HsrEndInformation? testData =
            await JsonSerializer.DeserializeAsync<HsrEndInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        UserGameData userGameData = GetTestUserGameData();

        Stream stream =
            await m_Service.GetEndGameCardImageAsync(EndGameMode.PureFiction, userGameData, testData!,
                await GetBuffMapAsync());
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    [Test]
    [TestCase("As_TestData_1.json")]
    [TestCase("As_TestData_2.json")]
    [TestCase("As_TestData_3.json")]
    public async Task GetEndGameCardAsync_BossChallengeTestData_MatchesGoldenImage(string testDataFileName)
    {
        HsrEndInformation? testData =
            await JsonSerializer.DeserializeAsync<HsrEndInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)), JsonOptions);
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        UserGameData userGameData = GetTestUserGameData();

        Stream stream =
            await m_Service.GetEndGameCardImageAsync(EndGameMode.ApocalypticShadow, userGameData, testData!,
                await GetBuffMapAsync());
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"HsrAs_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrAs_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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
            Level = 70,
            Region = "prod_official_asia",
            RegionName = "Asia"
        };
    }

    private async Task<Dictionary<int, Stream>> GetBuffMapAsync()
    {
        return new Dictionary<int, Stream>
        {
            { 3031341, await m_ImageRepository.DownloadFileToStreamAsync("Test_Intertextuality") }
        };
    }

    [Test]
    public async Task GeneratePureFictionGoldenImage()
    {
        HsrEndInformation? testData1 = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "Pf_TestData_1.json")));
        HsrEndInformation? testData2 = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "Pf_TestData_2.json")));
        HsrEndInformation? testData3 = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "Pf_TestData_3.json")));

        Stream image1 = await
        m_Service.GetEndGameCardImageAsync(EndGameMode.PureFiction,
        GetTestUserGameData(), testData1!, await GetBuffMapAsync()); Stream
        image2 = await
        m_Service.GetEndGameCardImageAsync(EndGameMode.PureFiction,
        GetTestUserGameData(), testData2!, await GetBuffMapAsync()); Stream
        image3 = await
        m_Service.GetEndGameCardImageAsync(EndGameMode.PureFiction,
        GetTestUserGameData(), testData3!, await GetBuffMapAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image1, Is.Not.Null);
            Assert.That(image2, Is.Not.Null); Assert.That(image3, Is.Not.Null);
        }

        await using FileStream fileStream1 =
        File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
        "TestAssets", "Pf_GoldenImage_1.jpg")); await using FileStream
        fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory,
        "Assets", "Hsr", "TestAssets", "Pf_GoldenImage_2.jpg")); await using
        FileStream fileStream3 =
        File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
        "TestAssets", "Pf_GoldenImage_3.jpg"));

        await image1.CopyToAsync(fileStream1);
        await image2.CopyToAsync(fileStream2);
        await image3.CopyToAsync(fileStream3);
    }

    [Test]
    public async Task GenerateBossChallengeGoldenImage()
    {
        HsrEndInformation? testData1 = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "As_TestData_1.json")), JsonOptions);
        HsrEndInformation? testData2 = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "As_TestData_2.json")), JsonOptions);
        HsrEndInformation? testData3 = await JsonSerializer.DeserializeAsync<HsrEndInformation>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "As_TestData_3.json")), JsonOptions);

        Stream image1 = await
        m_Service.GetEndGameCardImageAsync(EndGameMode.ApocalypticShadow,
        GetTestUserGameData(), testData1!, await GetBuffMapAsync()); Stream
        image2 = await
        m_Service.GetEndGameCardImageAsync(EndGameMode.ApocalypticShadow,
        GetTestUserGameData(), testData2!, await GetBuffMapAsync()); Stream
        image3 = await
        m_Service.GetEndGameCardImageAsync(EndGameMode.ApocalypticShadow,
        GetTestUserGameData(), testData3!, await GetBuffMapAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image1, Is.Not.Null);
            Assert.That(image2, Is.Not.Null); Assert.That(image3, Is.Not.Null);
        }

        await using FileStream fileStream1 =
        File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
        "TestAssets", "As_GoldenImage_1.jpg")); await using FileStream
        fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory,
        "Assets", "Hsr", "TestAssets", "As_GoldenImage_2.jpg")); await using
        FileStream fileStream3 =
        File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
        "TestAssets", "As_GoldenImage_3.jpg"));

        await image1.CopyToAsync(fileStream1);
        await image2.CopyToAsync(fileStream2);
        await image3.CopyToAsync(fileStream3);
    }
}
