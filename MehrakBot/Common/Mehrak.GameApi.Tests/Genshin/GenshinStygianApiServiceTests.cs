#region

using Mehrak.Domain.Shared.Models;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.GameApi.Tests.Genshin;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinStygianApiServiceTests
{
    private const string AvailableJson = """
        {
          "retcode": 0,
          "message": "OK",
          "data": {
            "is_unlock": true,
            "data": [
              {
                "schedule": {
                  "schedule_id": "s1",
                  "start_time": "1786000000",
                  "end_time": "1789000000",
                  "is_valid": true,
                  "name": "Stygian"
                },
                "single": {"best": null, "challenge": [], "has_data": true},
                "mp": {"best": null, "challenge": [], "has_data": false}
              }
            ]
          }
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private GenshinStygianApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new GenshinStygianApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<GenshinStygianApiService>>());
    }

    [Test]
    public async Task GetAsync_NotUnlocked_ReturnsLockedOutcome()
    {
        m_Handler.EnqueueJson(
            """{"retcode":0,"message":"OK","data":{"is_unlock":false,"data":null}}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Locked));
            Assert.That(result.Data.Payload, Is.Null);
        }
    }

    [Test]
    public async Task GetAsync_UnlockedEmptyData_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"is_unlock":true,"data":[]}}""");

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
    public async Task GetAsync_UnlockedNullData_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson("""{"retcode":0,"message":"OK","data":{"is_unlock":true,"data":null}}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_UnlockedRecordWithoutData_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "is_unlock": true,
                "data": [
                  {
                    "schedule": null,
                    "single": {"best": null, "challenge": null, "has_data": false},
                    "mp": {"best": null, "challenge": null, "has_data": false}
                  }
                ]
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
    public async Task GetAsync_UnlockedRecordWithNullSingle_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "is_unlock": true,
                "data": [{"schedule": null, "single": null, "mp": null}]
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
            var stygianPayload = payload!;
            Assert.That(stygianPayload.Data, Has.Count.EqualTo(1));
            var firstRecord = stygianPayload.Data![0];
            Assert.That(firstRecord.Single.HasData, Is.True);
            Assert.That(firstRecord.Schedule, Is.Not.Null);
        }
    }

    [TestCase("253402300800")]
    [TestCase("-62135596801")]
    [TestCase("9223372036854775807")]
    public async Task GetAsync_ScheduleOutsideUnixSecondsRange_ReturnsControlledInvalidResponse(
        string invalidTime)
    {
        m_Handler.EnqueueJson(CreatePayload(invalidTime, "1789000000"));

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
        m_Handler.EnqueueJson(CreatePayload(boundary, "1789000000"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
        }
    }

    [Test]
    public async Task GetAsync_NullTeamEntry_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload("1786000000", "1789000000",
            "[{\"name\":\"C\",\"teams\":[null],\"best_avatar\":[],\"monster\":{\"name\":\"M\",\"icon\":\"i\"}}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullBestAvatarEntry_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload("1786000000", "1789000000",
            "[{\"name\":\"C\",\"teams\":[],\"best_avatar\":[null],\"monster\":{\"name\":\"M\",\"icon\":\"i\"}}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_AvailableWithChallengeEntries_ReturnsPayload()
    {
        m_Handler.EnqueueJson(CreatePayload("1786000000", "1789000000",
            "[{\"name\":\"C\",\"teams\":[],\"best_avatar\":[],\"monster\":{\"name\":\"M\",\"icon\":\"i\"}}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
        }
    }

    [Test]
    public async Task GetAsync_FirstSingleWithoutDataButLaterRecordWithData_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson("""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "is_unlock": true,
                "data": [
                  {
                    "schedule": {"schedule_id": "s1", "start_time": "1786000000", "end_time": "1789000000", "is_valid": true, "name": "First"},
                    "single": {"best": null, "challenge": null, "has_data": false},
                    "mp": {"best": null, "challenge": null, "has_data": true}
                  },
                  {
                    "schedule": {"schedule_id": "s2", "start_time": "1786000000", "end_time": "1789000000", "is_valid": true, "name": "Later"},
                    "single": {"best": null, "challenge": [], "has_data": true},
                    "mp": {"best": null, "challenge": [], "has_data": true}
                  }
                ]
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
    public async Task GetAsync_FirstSingleWithDataButMultiWithoutData_ReturnsAvailable()
    {
        m_Handler.EnqueueJson(CreatePayload("1786000000", "1789000000"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
            var payload = result.Data.Payload;
            Assert.That(payload, Is.Not.Null);
            var available = payload!;
            Assert.That(available.Data![0].Single.HasData, Is.True);
            Assert.That(available.Data[0].Multi.HasData, Is.False);
        }
    }

    [Test]
    public async Task GetAsync_AuthRetcode_ReturnsUnauthorizedInsteadOfLocked()
    {
        m_Handler.EnqueueJson("""{"retcode":10001,"message":"upstream text"}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Contain("Invalid HoYoLAB UID or Cookies"));
            Assert.That(result.ErrorMessage, Does.Not.Contain("upstream text"));
        }
    }

    private static string CreatePayload(string startTime, string endTime, string challengeJson = "[]")
    {
        return $$"""
            {
              "retcode": 0,
              "message": "OK",
              "data": {
                "is_unlock": true,
                "data": [
                  {
                    "schedule": {
                      "schedule_id": "s1",
                      "start_time": "{{startTime}}",
                      "end_time": "{{endTime}}",
                      "is_valid": true,
                      "name": "Stygian"
                    },
                    "single": {"best": null, "challenge": {{challengeJson}}, "has_data": true},
                    "mp": {"best": null, "challenge": [], "has_data": false}
                  }
                ]
              }
            }
            """;
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }
}
