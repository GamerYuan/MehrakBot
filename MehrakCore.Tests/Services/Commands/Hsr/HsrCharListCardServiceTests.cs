using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr.CharList;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharListCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;

    private HsrCharListCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, new NullLogger<ImageRepository>());

        m_HttpClientMock = new Mock<HttpClient>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(m_HttpClientMock.Object);

        m_Service = new HsrCharListCardService(m_ImageRepository, Mock.Of<ILogger<HsrCharListCardService>>());
    }

    [Test]
    [TestCase("CharList_TestData_1.json")]
    public async Task GetCharListCardAsync_TestData_MatchesGoldenImage(string filename)
    {
        HsrBasicCharacterData? testData = await
            JsonSerializer.DeserializeAsync<HsrBasicCharacterData>(
                File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
            "Hsr", "CharList_TestData_1.json")), JsonOptions);

        Stream image = await m_Service.GetCharListCardAsync(GetTestUserGameData(),
            testData!.AvatarList!);

        Assert.That(image, Is.Not.Null);

        byte[] goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
                "TestAssets", filename.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        byte[] bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"HsrCharList_Data{Path.GetFileNameWithoutExtension(filename).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);
        // Save golden image to output folder for comparison
        string outputGoldenImagePath = Path.Combine(outputDirectory,
            $"HsrCharList_Data{Path.GetFileNameWithoutExtension(filename).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.EqualTo(goldenImage), "Generated image should match the golden image");
    }

    private static UserGameData GetTestUserGameData()
    {
        return new UserGameData
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 60,
            Region = "prod_official_asia",
            RegionName = "Asia"
        };
    }

    //[Test]
    //[TestCase("CharList_TestData_1.json")]
    //public async Task GenerateGoldenImage(string filename)
    //{
    //    HsrBasicCharacterData? testData = await
    //        JsonSerializer.DeserializeAsync<HsrBasicCharacterData>(
    //            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData",
    //        "Hsr", "CharList_TestData_1.json")), JsonOptions);

    // Stream image = await
    // m_Service.GetCharListCardAsync(GetTestUserGameData(), testData!.AvatarList!);

    // Assert.That(image, Is.Not.Null);

    // await using FileStream fileStream =
    // File.Create($"{Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr",
    // "TestAssets", Path.GetFileNameWithoutExtension(filename)
    // .Replace("TestData", "GoldenImage"))}.jpg");

    // await image.CopyToAsync(fileStream);

    //    await fileStream.FlushAsync();
    //}
}
