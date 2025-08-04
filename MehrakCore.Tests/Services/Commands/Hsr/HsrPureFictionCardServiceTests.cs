#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr.PureFiction;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

public class HsrPureFictionCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private HsrPureFictionCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new HsrPureFictionCardService(m_ImageRepository, NullLogger<HsrPureFictionCardService>.Instance);
    }

    [Test]
    [TestCase("Pf_TestData_1.json")]
    [TestCase("Pf_TestData_2.json")]
    [TestCase("Pf_TestData_3.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<HsrPureFictionInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var stream =
            await m_Service.GetFictionCardImageAsync(userGameData, testData!, await GetBuffMapAsync());
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrPf_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
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

    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     var testData1 = await JsonSerializer.DeserializeAsync<HsrPureFictionInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    //             "Pf_TestData_1.json")));
    //     var testData2 = await JsonSerializer.DeserializeAsync<HsrPureFictionInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    //             "Pf_TestData_2.json")));
    //     var testData3 = await JsonSerializer.DeserializeAsync<HsrPureFictionInformation>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    //             "Pf_TestData_3.json")));
    //
    //     var image1 =
    //         await m_Service.GetFictionCardImageAsync(GetTestUserGameData(), testData1!, await GetBuffMapAsync());
    //     var image2 =
    //         await m_Service.GetFictionCardImageAsync(GetTestUserGameData(), testData2!, await GetBuffMapAsync());
    //     var image3 =
    //         await m_Service.GetFictionCardImageAsync(GetTestUserGameData(), testData3!, await GetBuffMapAsync());
    //
    //     using (Assert.EnterMultipleScope())
    //     {
    //         Assert.That(image1, Is.Not.Null);
    //         Assert.That(image2, Is.Not.Null);
    //         Assert.That(image3, Is.Not.Null);
    //     }
    //
    //     await using var fileStream1 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    //         "TestAssets", "Pf_GoldenImage_1.jpg"));
    //     await using var fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    //         "TestAssets", "Pf_GoldenImage_2.jpg"));
    //     await using var fileStream3 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    //         "TestAssets", "Pf_GoldenImage_3.jpg"));
    //
    //     await image1.CopyToAsync(fileStream1);
    //     await image2.CopyToAsync(fileStream2);
    //     await image3.CopyToAsync(fileStream3);
    // }
}
