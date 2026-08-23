using System.Reflection;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.Hi3;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class Hi3CharacterApiServiceTests
{
    private const string OneCharacterJson = """
        {"retcode":0,"message":"OK","data":{"characters":[
          {"character":{"avatar":{"id":101,"name":"Char A","star":6,"level":80},
           "weapon":{"id":1,"name":"Weapon A","max_rarity":5,"rarity":5,"icon":"w.png","level":80},
           "stigmatas":[],"costumes":[]}}
        ]}}
        """;

    private const string DataWithRetcodeTemplate = """
        {{"retcode":{0},"message":"err","data":{{"characters":[
          {{"character":{{"avatar":{{"id":101,"name":"Char A","star":6,"level":80}},
           "weapon":{{"id":1,"name":"Weapon A","max_rarity":5,"rarity":5,"icon":"w.png","level":80}},
           "stigmatas":[],"costumes":[]}}}}
        ]}}}}
        """;

    private readonly FakeHttpMessageHandler m_Handler = new();
    private readonly Mock<ICacheService> m_Cache = new();

    private ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext> CreateService()
    {
        // Service class is internal and not InternalsVisibleTo this assembly; instantiate via reflection
        var type = typeof(GameRoleApiService).Assembly.GetType("Mehrak.GameApi.Hi3.Hi3CharacterApiService")
                   ?? throw new InvalidOperationException("Hi3CharacterApiService type not found");
        // Moq loose mocks return empty sequences for Task<IEnumerable<T>>, which the service treats as a cache hit
        m_Cache.Setup(c => c.GetAsync<IEnumerable<Hi3CharacterDetail>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Hi3CharacterDetail>?)null);
        var instance = Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [m_Handler.ToHttpClientFactory(), m_Cache.Object, LoggerMock.For(type)],
            null)!;
        return (ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>)instance;
    }

    [Test]
    public async Task GetAllCharactersAsync_RetcodeZero_ReturnsDeserializedCharacters()
    {
        m_Handler.EnqueueJson(OneCharacterJson);
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "100000001", "overseas01");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.ToList(), Has.Count.EqualTo(1));
            Assert.That(result.Data!.First().Avatar.Name, Is.EqualTo("Char A"));
            Assert.That(result.Data.First().Avatar.Star, Is.EqualTo(6));
            Assert.That(result.Data.First().Weapon.Name, Is.EqualTo("Weapon A"));
        });
        m_Cache.Verify(
            c => c.SetAsync(It.IsAny<ICacheEntry<IEnumerable<Hi3CharacterDetail>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllCharactersAsync_RetCode10001_ReturnsUnauthorized()
    {
        // Unlike Genshin/Hsr/Zzz, retcode is checked before the empty-data guard here
        m_Handler.EnqueueJson("""{"retcode":10001,"message":"auth error"}""");
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "100000001", "overseas01");

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
        var context = new CharacterApiContext(1, 100, "ltoken", "100000001", "overseas01");

        var result = await service.GetAllCharactersAsync(context);

        Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
    }
}
