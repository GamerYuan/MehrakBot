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
public class ZzzTowerApiServiceTests
{
    private const string AvailableJson = """
        {
          "retcode": 0,
          "message": "OK",
          "data": {
            "climbing_tower_s3": {
              "layer_info": {"climbing_tower_layer": 1, "total_score": 1, "medal_icon": "medal.png"},
              "mvp_info": {"floor_mvp_num": 1, "rank_percent": 1},
              "display_avatar_rank_list": [
                {
                  "avatar_id": 1,
                  "icon": "icon.png",
                  "name": "Synthetic",
                  "rarity": "S",
                  "rank_percent": 1,
                  "score": 1,
                  "display_rank": true,
                  "selected": true
                }
              ]
            }
          }
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private ZzzTowerApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new ZzzTowerApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<ZzzTowerApiService>>());
    }

    [Test]
    public async Task GetAsync_AbsentTowerNode_ReturnsEmptyOutcomeWithoutFabricatedPayload()
    {
        m_Handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{}}""");

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
    public async Task GetAsync_NullTowerNode_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"climbing_tower_s3":null}}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
        }
    }

    [Test]
    public async Task GetAsync_NullRankList_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "climbing_tower_s3": {
                  "layer_info": {"climbing_tower_layer": 1, "total_score": 1, "medal_icon": "medal.png"},
                  "mvp_info": {"floor_mvp_num": 1, "rank_percent": 1},
                  "display_avatar_rank_list": null
                }
              }
            }
            """);

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_EmptyRankList_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "climbing_tower_s3": {
                  "layer_info": {"climbing_tower_layer": 1, "total_score": 1, "medal_icon": "medal.png"},
                  "mvp_info": {"floor_mvp_num": 1, "rank_percent": 1},
                  "display_avatar_rank_list": []
                }
              }
            }
            """);

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
        }
    }

    [Test]
    public async Task GetAsync_NullAvatarRarity_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "climbing_tower_s3": {
                  "layer_info": {"climbing_tower_layer": 1, "total_score": 1, "medal_icon": "medal.png"},
                  "mvp_info": {"floor_mvp_num": 1, "rank_percent": 1},
                  "display_avatar_rank_list": [
                    {"avatar_id": 1, "icon": "icon.png", "name": "Synthetic", "rarity": null}
                  ]
                }
              }
            }
            """);

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
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "climbing_tower_s3": {
                  "layer_info": {"climbing_tower_layer": 1, "total_score": 1, "medal_icon": "medal.png"},
                  "mvp_info": {"floor_mvp_num": 1, "rank_percent": 1},
                  "display_avatar_rank_list": [
                    {"avatar_id": 1, "icon": "icon.png", "name": "Synthetic", "rarity": ""}
                  ]
                }
              }
            }
            """);

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
            Assert.That(payload!.DisplayAvatarRankList, Has.Count.EqualTo(1));
            Assert.That(payload.LayerInfo, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_AuthRetcode_ReturnsUnauthorized()
    {
        m_Handler.EnqueueJson("""{"retcode":10001,"message":"upstream text"}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Not.Contain("upstream text"));
        }
    }

    [Test]
    public async Task GetAsync_UnknownRetcode_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson("""{"retcode":12345,"message":"upstream text"}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Does.Not.Contain("upstream text"));
        }
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }
}
