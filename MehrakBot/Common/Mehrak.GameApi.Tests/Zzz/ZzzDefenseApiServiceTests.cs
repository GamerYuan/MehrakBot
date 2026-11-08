#region

using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.GameApi.Tests.Zzz;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzDefenseApiServiceTests
{
    private const string AvailableJson = """
        {
          "retcode": 0,
          "message": "OK",
          "data": {
            "hadal_ver": "v2",
            "hadal_info_v2": {
              "zone_id": 1,
              "hadal_begin_time": {"year": 2026, "month": 1, "day": 1, "hour": 0, "minute": 0},
              "hadal_end_time": {"year": 2026, "month": 2, "day": 1, "hour": 0, "minute": 0},
              "pass_fifth_floor": true,
              "brief": {
                "cur_period_zone_layer_count": 1,
                "score": 1,
                "rank_percent": 1,
                "rating": "S",
                "max_score": 1
              },
              "fitfh_layer_detail": {
                "layer_challenge_info_list": [
                  {
                    "layer_id": 1,
                    "score": 1,
                    "avatar_list": [{"id": 1, "rarity": "S", "role_square_url": "avatar.png"}],
                    "buddy": null,
                    "battle_time": 1,
                    "rating": "S",
                    "monster_pic": "monster.png",
                    "max_score": 1
                  }
                ]
              },
              "begin_time": "1786000000",
              "end_time": "1789000000"
            },
            "nick_name": "Synthetic",
            "icon": "icon.png"
          }
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private ZzzDefenseApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new ZzzDefenseApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<ZzzDefenseApiService>>());
    }

    [Test]
    public async Task GetAsync_NullHadalInfo_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(
            """{"retcode":0,"message":"OK","data":{"hadal_ver":"v2","hadal_info_v2":null,"nick_name":"N","icon":"i"}}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_NullBrief_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreateDefensePayload("null",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
            Assert.That(result.Data.Payload, Is.Null);
        }
    }

    [Test]
    public async Task GetAsync_NullFifthLayerDetail_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            null));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
        }
    }

    [TestCase("253402300800")]
    [TestCase("-62135596801")]
    [TestCase("9223372036854775807")]
    public async Task GetAsync_ScheduleOutsideUnixSecondsRange_ReturnsControlledInvalidResponse(
        string invalidTime)
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]",
            invalidTime, "1789000000"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [TestCase("253402300799")]
    [TestCase("-62135596800")]
    public async Task GetAsync_ScheduleAtUnixSecondsBounds_ReturnsAvailable(string boundary)
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]",
            boundary, "1789000000"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
        }
    }
    [Test]
    public async Task GetAsync_MalformedLayerChallengeInfo_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":null,\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullBriefRating_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":null,\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_EmptyBriefRating_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullChallengeRating_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":null,\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_EmptyChallengeRating_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullAvatarRarity_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[{\"id\":1,\"rarity\":null,\"role_square_url\":\"a.png\"}],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_EmptyAvatarRarity_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreateDefensePayload(
            "{\"cur_period_zone_layer_count\":1,\"score\":1,\"rank_percent\":1,\"rating\":\"S\",\"max_score\":1}",
            "[{\"layer_id\":1,\"score\":1,\"avatar_list\":[{\"id\":1,\"rarity\":\"\",\"role_square_url\":\"a.png\"}],\"buddy\":null,\"battle_time\":1," +
            "\"rating\":\"S\",\"monster_pic\":\"m.png\",\"max_score\":1}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }
    [Test]
    public async Task GetAsync_Available_ReturnsPayload()
    {
        m_Handler.EnqueueJson(AvailableJson);

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
            var payload = result.Data.Payload;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.Brief, Is.Not.Null);
            Assert.That(payload.FifthLayerDetail!.LayerChallengeInfoList, Has.Count.EqualTo(1));
        }
    }

    private static string CreateDefensePayload(string briefJson, string? fifthLayerListJson,
        string beginTime = "1786000000", string endTime = "1789000000")
    {
        var fifthLayerDetail = fifthLayerListJson is null
            ? "null"
            : $"{{\"layer_challenge_info_list\":{fifthLayerListJson}}}";

        return "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"hadal_ver\":\"v2\",\"hadal_info_v2\":{" +
               "\"zone_id\":1,\"hadal_begin_time\":{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0}," +
               "\"hadal_end_time\":{\"year\":2026,\"month\":2,\"day\":1,\"hour\":0,\"minute\":0}," +
               "\"pass_fifth_floor\":true," +
               $"\"brief\":{briefJson},\"fitfh_layer_detail\":{fifthLayerDetail}," +
               "\"begin_time\":\"" + beginTime + "\",\"end_time\":\"" + endTime + "\"}," +
               "\"nick_name\":\"Synthetic\",\"icon\":\"icon.png\"}}";
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }
}
