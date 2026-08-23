using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.Genshin;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinCharacterApiServiceTests
{
    private const string TwoCharactersJson = """
        {"retcode":0,"message":"OK","data":{"list":[
          {"id":10000002,"icon":"a.png","name":"Char A","level":90,"weapon":{"id":11101,"icon":"w.png","name":"Sword"}},
          {"id":10000046,"icon":"b.png","name":"Char B","level":80,"weapon":{"id":15502,"icon":"w2.png","name":"Polearm"}}
        ]}}
        """;

    private const string DataWithRetcodeTemplate = """
        {{"retcode":{0},"message":"err","data":{{"list":[
          {{"id":10000002,"icon":"a.png","name":"Char A","level":90,"weapon":{{"id":11101,"icon":"w.png","name":"Sword"}}}}
        ]}}}}
        """;

    private readonly FakeHttpMessageHandler m_Handler = new();
    private readonly Mock<ICacheService> m_Cache = new();

    private GenshinCharacterApiService CreateService()
    {
        // Moq loose mocks return empty sequences for Task<IEnumerable<T>>, which the service treats as a cache hit
        m_Cache.Setup(c => c.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<GenshinBasicCharacterData>?)null);
        return new GenshinCharacterApiService(m_Cache.Object, m_Handler.ToHttpClientFactory(),
            new Mock<ILogger<GenshinCharacterApiService>>().Object);
    }

    [Test]
    public async Task GetAllCharactersAsync_RetcodeZero_ReturnsDeserializedCharacters()
    {
        m_Handler.EnqueueJson(TwoCharactersJson);
        var service = CreateService();
        var context = new GenshinCharacterApiContext(1, 100, "ltoken", "900000001", "os_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.ToList(), Has.Count.EqualTo(2));
            Assert.That(result.Data!.First().Name, Is.EqualTo("Char A"));
            Assert.That(result.Data.First().Weapon.Name, Is.EqualTo("Sword"));
        });
        m_Cache.Verify(
            c => c.SetAsync(It.IsAny<ICacheEntry<IEnumerable<GenshinBasicCharacterData>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllCharactersAsync_RetCode10001WithData_ReturnsUnauthorized()
    {
        m_Handler.EnqueueJson(string.Format(DataWithRetcodeTemplate, 10001));
        var service = CreateService();
        var context = new GenshinCharacterApiContext(1, 100, "ltoken", "900000001", "os_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Contain("Invalid HoYoLAB UID or Cookies"));
        });
    }

    [Test]
    public async Task GetAllCharactersAsync_RetCode10001WithoutData_ReturnsExternalServerError()
    {
        // Quirk: empty-data check precedes retcode handling, so bare 10001 never maps to Unauthorized
        m_Handler.EnqueueJson("""{"retcode":10001,"message":"auth error"}""");
        var service = CreateService();
        var context = new GenshinCharacterApiContext(1, 100, "ltoken", "900000001", "os_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        });
    }

    [Test]
    public async Task GetAllCharactersAsync_UnknownRetcode_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson(string.Format(DataWithRetcodeTemplate, 1234));
        var service = CreateService();
        var context = new GenshinCharacterApiContext(1, 100, "ltoken", "900000001", "os_asia");

        var result = await service.GetAllCharactersAsync(context);

        Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
    }
}
