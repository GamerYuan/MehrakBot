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
public class ZzzAssaultApiServiceTests
{
    private const string AvailableJson = """
        {
          "retcode": 0,
          "message": "OK",
          "data": {
            "start_time": {"year": 2026, "month": 1, "day": 1, "hour": 0, "minute": 0, "second": 0},
            "end_time": {"year": 2026, "month": 2, "day": 1, "hour": 0, "minute": 0, "second": 0},
            "rank_percent": 0,
            "list": [
              {"score": 1, "star": 1, "boss": [{"name": "Boss", "icon": "boss.png", "bg_icon": "boss_bg.png"}], "avatar_list": [{"id": 1, "rarity": "S", "role_square_url": "avatar.png"}], "buffer": [{"name": "Buff", "icon": "buff.png"}], "buddy": null}
            ],
            "has_data": true,
            "total_score": 1,
            "total_star": 1,
            "has_hard": false,
            "hard_list": [],
            "hard_rank_percent": 0
          }
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private ZzzAssaultApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new ZzzAssaultApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<ZzzAssaultApiService>>());
    }

    [Test]
    public async Task GetAsync_NoData_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: false, list: "[]"));

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
    public async Task GetAsync_HasDataWithNullList_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true, list: "null"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_HasDataWithEmptyList_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true, list: "[]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
        }
    }

    [Test]
    public async Task GetAsync_MalformedFloor_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[],\"avatar_list\":null,\"buffer\":[],\"buddy\":null}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [TestCase(0, 1, 1)]
    [TestCase(2026, 13, 1)]
    [TestCase(2026, 2, 30)]
    public async Task GetAsync_InvalidStartTimeCalendar_ReturnsControlledInvalidResponse(int year, int month,
        int day)
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list: "[{\"score\":1,\"star\":1,\"boss\":[],\"avatar_list\":[],\"buffer\":[],\"buddy\":null}]",
            startTime: $"{{\"year\":{year},\"month\":{month},\"day\":{day},\"hour\":0,\"minute\":0,\"second\":0}}"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [TestCase(2026, 13, 1)]
    [TestCase(2026, 2, 30)]
    public async Task GetAsync_InvalidEndTimeCalendar_ReturnsControlledInvalidResponse(int year, int month, int day)
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list: "[{\"score\":1,\"star\":1,\"boss\":[],\"avatar_list\":[],\"buffer\":[],\"buddy\":null}]",
            endTime: $"{{\"year\":{year},\"month\":{month},\"day\":{day},\"hour\":0,\"minute\":0,\"second\":0}}"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }
    [Test]
    public async Task GetAsync_NullBossName_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[{\"name\":null,\"icon\":\"i\",\"bg_icon\":\"b\"}],\"avatar_list\":[],\"buffer\":[],\"buddy\":null}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullBuffName_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[],\"avatar_list\":[],\"buffer\":[{\"name\":null,\"icon\":\"i\"}],\"buddy\":null}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_AvailableWithBossAndBuff_ReturnsPayload()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[{\"name\":\"Boss\",\"icon\":\"i\",\"bg_icon\":\"b\"}],\"avatar_list\":[],\"buffer\":[{\"name\":\"Buff\",\"icon\":\"i\"}],\"buddy\":null}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
        }
    }
    [Test]
    public async Task GetAsync_EmptyBossCollection_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[],\"avatar_list\":[],\"buffer\":[{\"name\":\"Buff\",\"icon\":\"i\"}],\"buddy\":null}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_EmptyBuffCollection_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[{\"name\":\"Boss\",\"icon\":\"i\",\"bg_icon\":\"b\"}],\"avatar_list\":[],\"buffer\":[],\"buddy\":null}]"));

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
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[{\"name\":\"Boss\",\"icon\":\"i\",\"bg_icon\":\"b\"}],\"avatar_list\":[{\"id\":1,\"rarity\":null,\"role_square_url\":\"a.png\"}],\"buffer\":[{\"name\":\"Buff\",\"icon\":\"i\"}],\"buddy\":null}]"));

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
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            list:
            "[{\"score\":1,\"star\":1,\"boss\":[{\"name\":\"Boss\",\"icon\":\"i\",\"bg_icon\":\"b\"}],\"avatar_list\":[{\"id\":1,\"rarity\":\"\",\"role_square_url\":\"a.png\"}],\"buffer\":[{\"name\":\"Buff\",\"icon\":\"i\"}],\"buddy\":null}]"));

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
            Assert.That(payload!.HasData, Is.True);
            Assert.That(payload.List, Has.Count.EqualTo(1));
        }
    }

    private static string CreatePayload(bool hasData, string list,
        string startTime = "{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0,\"second\":0}",
        string endTime = "{\"year\":2026,\"month\":2,\"day\":1,\"hour\":0,\"minute\":0,\"second\":0}")
    {
        return $"{{\"retcode\":0,\"message\":\"OK\",\"data\":{{\"start_time\":{startTime}," +
               $"\"end_time\":{endTime},\"rank_percent\":0,\"list\":{list}," +
               $"\"has_data\":{(hasData ? "true" : "false")}," +
               "\"total_score\":1,\"total_star\":1,\"has_hard\":false,\"hard_list\":[],\"hard_rank_percent\":0}}";
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }
}
