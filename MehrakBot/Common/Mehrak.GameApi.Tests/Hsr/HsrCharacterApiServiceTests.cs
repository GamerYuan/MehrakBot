using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.Hsr;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrCharacterApiServiceTests
{
    private const string OneAvatarJson = """
        {"retcode":0,"message":"OK","data":{"avatar_list":[
          {"id":1001,"level":80,"name":"Char A","element":"Fire","icon":"a.png","image":"i.png",
           "relics":[],"ornaments":[],"ranks":[],"properties":[],"skills":[]}
        ],"equip_wiki":{},"relic_wiki":{}}}
        """;

    private const string DataWithRetcodeTemplate = """
        {{"retcode":{0},"message":"err","data":{{"avatar_list":[
          {{"id":1001,"level":80,"name":"Char A","element":"Fire","icon":"a.png","image":"i.png",
           "relics":[],"ornaments":[],"ranks":[],"properties":[],"skills":[]}}
        ],"equip_wiki":{{}},"relic_wiki":{{}}}}}}
        """;

    private readonly FakeHttpMessageHandler m_Handler = new();
    private readonly Mock<ICacheService> m_Cache = new();

    private HsrCharacterApiService CreateService()
    {
        // Moq loose mocks return empty sequences for Task<IEnumerable<T>>, which the service treats as a cache hit
        m_Cache.Setup(c => c.GetAsync<IEnumerable<HsrBasicCharacterData>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<HsrBasicCharacterData>?)null);
        return new HsrCharacterApiService(m_Handler.ToHttpClientFactory(), m_Cache.Object,
            new Mock<ILogger<HsrCharacterApiService>>().Object);
    }

    [Test]
    public async Task GetAllCharactersAsync_RetcodeZero_ReturnsDeserializedAvatars()
    {
        m_Handler.EnqueueJson(OneAvatarJson);
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "800000001", "prod_official_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.ToList(), Has.Count.EqualTo(1));
            Assert.That(result.Data!.First().AvatarList[0].Name, Is.EqualTo("Char A"));
        });
        m_Cache.Verify(
            c => c.SetAsync(It.IsAny<ICacheEntry<IEnumerable<HsrBasicCharacterData>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllCharactersAsync_RetCode10001WithData_ReturnsUnauthorized()
    {
        m_Handler.EnqueueJson(string.Format(DataWithRetcodeTemplate, 10001));
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "800000001", "prod_official_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Contain("Invalid HoYoLAB UID or Cookies"));
        });
    }

    [Test]
    public async Task GetAllCharactersAsync_UnknownRetcode_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson(string.Format(DataWithRetcodeTemplate, 1234));
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "800000001", "prod_official_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
    }
}
