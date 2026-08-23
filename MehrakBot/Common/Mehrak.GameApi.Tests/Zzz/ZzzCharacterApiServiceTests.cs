using System.Reflection;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.GameApi.Tests.Zzz;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzCharacterApiServiceTests
{
    private const string OneAvatarJson = """
        {"retcode":0,"message":"OK","data":{"avatar_list":[
          {"id":1011,"level":60,"name_mi18n":"Char A","full_name_mi18n":"Char A Full",
           "camp_name_mi18n":"Cunning Hares","element_type":201,"avatar_profession":4,
           "rarity":"S","group_icon_path":"g.png","hollow_icon_path":"h.png","rank":6,
           "is_chosen":false,"role_square_url":"r.png","sub_element_type":0,"awaken_state":"0"}
        ]}}
        """;

    private const string DataWithRetcodeTemplate = """
        {{"retcode":{0},"message":"err","data":{{"avatar_list":[
          {{"id":1011,"level":60,"name_mi18n":"Char A","full_name_mi18n":"Char A Full",
           "camp_name_mi18n":"Cunning Hares","element_type":201,"avatar_profession":4,
           "rarity":"S","group_icon_path":"g.png","hollow_icon_path":"h.png","rank":6,
           "is_chosen":false,"role_square_url":"r.png","sub_element_type":0,"awaken_state":"0"}}
        ]}}}}
        """;

    private readonly FakeHttpMessageHandler m_Handler = new();
    private readonly Mock<ICacheService> m_Cache = new();

    private ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext> CreateService()
    {
        // Service class is internal and not InternalsVisibleTo this assembly; instantiate via reflection
        var type = typeof(GameRoleApiService).Assembly.GetType("Mehrak.GameApi.Zzz.ZzzCharacterApiService")
                   ?? throw new InvalidOperationException("ZzzCharacterApiService type not found");
        // Moq loose mocks return empty sequences for Task<IEnumerable<T>>, which the service treats as a cache hit
        m_Cache.Setup(c => c.GetAsync<IEnumerable<ZzzBasicAvatarData>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ZzzBasicAvatarData>?)null);
        var instance = Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [m_Cache.Object, m_Handler.ToHttpClientFactory(), LoggerMock.For(type)],
            null)!;
        return (ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>)instance;
    }

    [Test]
    public async Task GetAllCharactersAsync_RetcodeZero_ReturnsDeserializedAvatars()
    {
        m_Handler.EnqueueJson(OneAvatarJson);
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "600000001", "prod_gf_jp");

        var result = await service.GetAllCharactersAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.First().Name, Is.EqualTo("Char A"));
            Assert.That(result.Data.First().Level, Is.EqualTo(60));
        });
        m_Cache.Verify(
            c => c.SetAsync(It.IsAny<ICacheEntry<IEnumerable<ZzzBasicAvatarData>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAllCharactersAsync_RetCode10001WithData_ReturnsUnauthorized()
    {
        m_Handler.EnqueueJson(string.Format(DataWithRetcodeTemplate, 10001));
        var service = CreateService();
        var context = new CharacterApiContext(1, 100, "ltoken", "600000001", "prod_gf_jp");

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
        var context = new CharacterApiContext(1, 100, "ltoken", "600000001", "prod_gf_jp");

        var result = await service.GetAllCharactersAsync(context);

        Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
    }
}
