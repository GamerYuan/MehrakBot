#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Genshin;

public class GenshinImageUpdaterServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData");

    private MongoDbRunner m_Runner;
    private Mock<MongoDbService> m_DbMock;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;

    private ImageRepository m_ImageRepo;

    [SetUp]
    public void Setup()
    {
        m_Runner = MongoDbRunner.Start();

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["MongoDB:ConnectionString"] = m_Runner.ConnectionString,
            ["MongoDB:DatabaseName"] = "TestDb"
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        m_DbMock = new Mock<MongoDbService>(config, new Mock<ILogger<MongoDbService>>().Object);

        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientMock = new Mock<HttpClient>();
        m_ImageRepo = new ImageRepository(m_DbMock.Object, new Mock<ILogger<ImageRepository>>().Object);

        m_HttpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(m_HttpClientMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_Runner.Dispose();
    }

    [Test]
    public async Task UpdateDataAsync_ShouldDownloadMissingImages()
    {
        // Arrange
        var service = new GenshinImageUpdaterService(
            m_ImageRepo,
            m_HttpClientFactoryMock.Object,
            NullLogger<GenshinImageUpdaterService>.Instance);

        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));

        Assert.That(characterDetail, Is.Not.Null);
        var characterInfo = characterDetail.List[0];
        Assert.That(characterInfo, Is.Not.Null);

        // Act
        await service.UpdateDataAsync(characterInfo, characterDetail.AvatarWiki);

        await Assert.MultipleAsync(async () =>
        {
            // Assert
            Assert.That(await m_ImageRepo.FileExistsAsync($"genshin_{characterInfo.Base.Id}"),
                Is.True);
            Assert.That(await m_ImageRepo.FileExistsAsync($"genshin_{characterInfo.Weapon.Id}"), Is.True);

            foreach (var id in characterInfo.Skills.Select(x => x.SkillId))
                Assert.That(await m_ImageRepo.FileExistsAsync($"genshin_{id}"), Is.True);

            foreach (var id in characterInfo.Constellations.Select(x => x.Id))
                Assert.That(await m_ImageRepo.FileExistsAsync($"genshin_{id}"), Is.True);

            foreach (var id in characterInfo.Relics.Select(x => x.Id))
                Assert.That(await m_ImageRepo.FileExistsAsync($"genshin_{id}"), Is.True);
        });
    }

    [Test]
    public async Task UpdateDataAsync_ShouldNotDownloadExistingImages()
    {
        // Arrange
        var service = new GenshinImageUpdaterService(
            m_ImageRepo,
            m_HttpClientFactoryMock.Object,
            NullLogger<GenshinImageUpdaterService>.Instance);

        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));

        Assert.That(characterDetail, Is.Not.Null);

        // Simulate that the image already exists in the repository
        await m_ImageRepo.UploadFileAsync($"genshin_{characterDetail.List[0].Base.Id}", new MemoryStream());

        // Act
        await service.UpdateDataAsync(characterDetail.List[0], characterDetail.AvatarWiki);

        // Assert
        var result = await m_ImageRepo.FileExistsAsync($"genshin_{characterDetail.List[0].Base.Id}");
        Assert.That(result, Is.True);
    }
}
