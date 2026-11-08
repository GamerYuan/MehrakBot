#region

using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.GameApi.Tests.Hsr;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrEndGameApiServiceTests
{
    private const string AvailableJson = """
        {
          "retcode": 0,
          "message": "OK",
          "data": {
            "groups": [
              {"schedule_id": 1, "begin_time": {"year": 2026, "month": 1, "day": 1, "hour": 0, "minute": 0}, "end_time": {"year": 2026, "month": 2, "day": 1, "hour": 0, "minute": 0}, "name_mi18n": "Pure Fiction"}
            ],
            "star_num": 0,
            "max_floor": "1",
            "battle_num": 1,
            "has_data": true,
            "all_floor_detail": [
              {
                "name": "Floor 1",
                "node_1": {
                  "challenge_time": {"year": 2026, "month": 1, "day": 1, "hour": 0, "minute": 0},
                  "avatars": [],
                  "buff": {"id": 1, "name_mi18n": "Buff", "desc_mi18n": "Desc", "icon": "buff.png"}
                }
              }
            ],
            "max_floor_id": 1,
            "extra_star_num": 0
          }
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private HsrEndGameApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new HsrEndGameApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<HsrEndGameApiService>>());
    }

    [Test]
    public async Task GetAsync_NoData_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: false,
            allFloorDetail: "null", groups: "[]"));

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
    public async Task GetAsync_HasDataWithNullAllFloorDetail_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            allFloorDetail: "null", groups: "[{\"schedule_id\":1,\"begin_time\":{},\"end_time\":{},\"name_mi18n\":\"PF\"}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_AllFloorDetailsWithoutNodes_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            allFloorDetail: "[{\"name\":\"Floor 1\"}]",
            groups: "[{\"schedule_id\":1,\"begin_time\":{},\"end_time\":{},\"name_mi18n\":\"PF\"}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
        }
    }

    [Test]
    public async Task GetAsync_NullNodeAvatarEntry_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            allFloorDetail:
            "[{\"name\":\"Floor 1\",\"node_1\":{\"challenge_time\":{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0},\"avatars\":[null],\"buff\":{\"id\":1,\"name_mi18n\":\"B\",\"desc_mi18n\":\"D\",\"icon\":\"i\"}}}]",
            groups:
            "[{\"schedule_id\":1,\"begin_time\":{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0},\"end_time\":{\"year\":2026,\"month\":2,\"day\":1,\"hour\":0,\"minute\":0},\"name_mi18n\":\"PF\"}]"));

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
    public async Task GetAsync_InvalidGroupBeginTimeCalendar_ReturnsControlledInvalidResponse(int year, int month,
        int day)
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            allFloorDetail:
            "[{\"name\":\"Floor 1\",\"node_1\":{\"challenge_time\":{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0},\"avatars\":[],\"buff\":{\"id\":1,\"name_mi18n\":\"B\",\"desc_mi18n\":\"D\",\"icon\":\"i\"}}}]",
            groups:
            $"[{{\"schedule_id\":1,\"begin_time\":{{\"year\":{year},\"month\":{month},\"day\":{day},\"hour\":0,\"minute\":0}},\"end_time\":{{\"year\":2026,\"month\":2,\"day\":1,\"hour\":0,\"minute\":0}},\"name_mi18n\":\"PF\"}}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [TestCase(2026, 13, 1)]
    [TestCase(2026, 2, 30)]
    public async Task GetAsync_InvalidGroupEndTimeCalendar_ReturnsControlledInvalidResponse(int year, int month,
        int day)
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            allFloorDetail:
            "[{\"name\":\"Floor 1\",\"node_1\":{\"challenge_time\":{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0},\"avatars\":[],\"buff\":{\"id\":1,\"name_mi18n\":\"B\",\"desc_mi18n\":\"D\",\"icon\":\"i\"}}}]",
            groups:
            $"[{{\"schedule_id\":1,\"begin_time\":{{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0}},\"end_time\":{{\"year\":{year},\"month\":{month},\"day\":{day},\"hour\":0,\"minute\":0}},\"name_mi18n\":\"PF\"}}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }
    [Test]
    public async Task GetAsync_AvailableWithoutGroups_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(hasData: true,
            allFloorDetail:
            "[{\"name\":\"Floor 1\",\"node_1\":{\"challenge_time\":{},\"avatars\":[],\"buff\":{\"id\":1,\"name_mi18n\":\"B\",\"desc_mi18n\":\"D\",\"icon\":\"i\"}}}]",
            groups: "[]"));

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
            Assert.That(payload.Groups, Has.Count.EqualTo(1));
            Assert.That(payload.AllFloorDetail, Has.Count.EqualTo(1));
        }
    }

    private static string CreatePayload(bool hasData, string allFloorDetail, string groups)
    {
        return $"{{\"retcode\":0,\"message\":\"OK\",\"data\":{{\"groups\":{groups},\"star_num\":0," +
               $"\"max_floor\":\"1\",\"battle_num\":1,\"has_data\":{(hasData ? "true" : "false")}," +
               $"\"all_floor_detail\":{allFloorDetail},\"max_floor_id\":1,\"extra_star_num\":0}}}}";
    }

    private static HsrEndGameApiContext CreateContext()
    {
        return new HsrEndGameApiContext(1, 100, "valid-token", "900000001", "os_asia",
            HsrEndGameMode.PureFiction);
    }
}
