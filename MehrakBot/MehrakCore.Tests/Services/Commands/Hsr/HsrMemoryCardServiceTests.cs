#region

using System.Text.Json;
using Mehrak.GameApi.Hsr.Types;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrMemoryCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private HsrMemoryCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new HsrMemoryCardService(m_ImageRepository, NullLogger<HsrMemoryCardService>.Instance);
        await m_Service.InitializeAsync();
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

    [Test]
    [TestCase("Moc_TestData_1.json")]
    [TestCase("Moc_TestData_2.json")]
    [TestCase("Moc_TestData_3.json")]
    [TestCase("Moc_TestData_4.json")]
    [TestCase("Moc_TestData_5.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        HsrMemoryInformation? testData =
            await JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
                File.OpenRead(Path.Combine(TestDataPath, "Hsr", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        UserGameData userGameData = GetTestUserGameData();

        Stream stream =
            await m_Service.GetMemoryCardImageAsync(userGameData, testData!);
        MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"HsrMoc_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrMoc_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
    }

    // [Test] public async Task GenerateGoldenImage() { var testData1 = await
    // JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    // "Moc_TestData_1.json"))); var testData2 = await
    // JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    // "Moc_TestData_2.json"))); var testData3 = await
    // JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    // "Moc_TestData_3.json"))); var testData4 = await
    // JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
    // "Moc_TestData_4.json"))); var testData5 = await
    // JsonSerializer.DeserializeAsync<HsrMemoryInformation>(
    // File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", "Moc_TestData_5.json")));
    //
    // var image1 = await
    // m_Service.GetMemoryCardImageAsync(GetTestUserGameData(), testData1!); var
    // image2 = await m_Service.GetMemoryCardImageAsync(GetTestUserGameData(),
    // testData2!); var image3 = await
    // m_Service.GetMemoryCardImageAsync(GetTestUserGameData(), testData3!); var
    // image4 = await m_Service.GetMemoryCardImageAsync(GetTestUserGameData(),
    // testData4!); var image5 = await
    // m_Service.GetMemoryCardImageAsync(GetTestUserGameData(), testData5!);
    //
    // using (Assert.EnterMultipleScope()) { Assert.That(image1, Is.Not.Null);
    // Assert.That(image2, Is.Not.Null); Assert.That(image3, Is.Not.Null);
    // Assert.That(image4, Is.Not.Null); Assert.That(image5, Is.Not.Null); }
    //
    // await using var fileStream1 =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", "Moc_GoldenImage_1.jpg")); await using var fileStream2 =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", "Moc_GoldenImage_2.jpg")); await using var fileStream3 =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", "Moc_GoldenImage_3.jpg")); await using var fileStream4 =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", "Moc_GoldenImage_4.jpg")); await using var fileStream5 =
    // File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", "Moc_GoldenImage_5.jpg"));
    //
    // await image1.CopyToAsync(fileStream1); await
    // image2.CopyToAsync(fileStream2); await image3.CopyToAsync(fileStream3);
    // await image4.CopyToAsync(fileStream4); await
    // image5.CopyToAsync(fileStream5); }
}