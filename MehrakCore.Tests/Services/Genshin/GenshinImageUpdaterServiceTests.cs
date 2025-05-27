#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinImageUpdaterServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData");

    private MongoTestHelper m_MongoTestHelper;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpClient> m_HttpClientMock;

    private ImageRepository m_ImageRepo;

    [SetUp]
    public void Setup()
    {
        m_MongoTestHelper = new MongoTestHelper();

        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientMock = new Mock<HttpClient>();
        m_ImageRepo =
            new ImageRepository(m_MongoTestHelper.MongoDbService, new Mock<ILogger<ImageRepository>>().Object);

        m_HttpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(m_HttpClientMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_MongoTestHelper.Dispose();
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
