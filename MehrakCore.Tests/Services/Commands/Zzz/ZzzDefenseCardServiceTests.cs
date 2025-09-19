using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Zzz.Defense;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace MehrakCore.Tests.Services.Commands.Zzz;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzDefenseCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ImageRepository m_ImageRepository;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;
    private ZzzDefenseCardService m_Service;

    [SetUp]
    public async Task Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, new NullLogger<ImageRepository>());

        m_HttpClientMock = new Mock<HttpClient>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(m_HttpClientMock.Object);

        m_Service = new ZzzDefenseCardService(m_ImageRepository,
            NullLogger<ZzzDefenseCardService>.Instance);
        await m_Service.InitializeAsync();
    }

    private static UserGameData GetUserGameDataa()
    {
        return new()
        {
            GameUid = "1300000000",
            GameBiz = "nap_global",
            Nickname = "Test",
            Region = "prod_gf_jp"
        };
    }

    [Test]
    [TestCase("Shiyu_TestData_1.json")]
    [TestCase("Shiyu_TestData_2.json")]
    [TestCase("Shiyu_TestData_3.json")]
    public async Task GenerateGoldenImage(string testData)
    {
        ZzzDefenseData? defenseData =
                JsonSerializer.Deserialize<ZzzDefenseData>(
                    await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(defenseData, Is.Not.Null);

        Stream image = await m_Service.GetDefenseCardAsync(defenseData,
            GetUserGameDataa(), Regions.Asia);
        FileStream fileStream = File.OpenWrite(
            $"{Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
                Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage"))}.jpg");

        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
    }
}
