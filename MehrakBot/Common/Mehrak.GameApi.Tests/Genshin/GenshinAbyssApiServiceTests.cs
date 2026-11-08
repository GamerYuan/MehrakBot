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
public class GenshinAbyssApiServiceTests
{
    private const string RankEntryJson =
        "[{\"avatar_id\":1,\"avatar_icon\":\"https://example.invalid/avatar.png\",\"value\":1,\"rarity\":5}]";

    private const string ValidFloorsJson =
        "\"floors\":[{\"index\":12,\"levels\":[{\"index\":1,\"battles\":[{\"index\":1,\"avatars\":[]}]}]}]";

    private static string AvailableJson => CreatePayload(ValidFloorsJson);

    private FakeHttpMessageHandler m_Handler = null!;
    private GenshinAbyssApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new GenshinAbyssApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<GenshinAbyssApiService>>());
    }

    [Test]
    public async Task GetAsync_NullFloors_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload("\"floors\":null"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_EmptyFloors_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreatePayload("\"floors\":[]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.Availability, Is.EqualTo(GameApiAvailability.Empty));
            Assert.That(result.Data.Payload, Is.Null);
        }
    }

    [TestCase("253402300800", "1789000000")]
    [TestCase("1786000000", "-62135596801")]
    [TestCase("9223372036854775807", "1789000000")]
    public async Task GetAsync_ScheduleOutsideUnixSecondsRange_ReturnsControlledInvalidResponse(
        string startTime, string endTime)
    {
        m_Handler.EnqueueJson(CreatePayload(ValidFloorsJson, startTime, endTime));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [TestCase("253402300799", "1789000000")]
    [TestCase("1786000000", "-62135596800")]
    public async Task GetAsync_ScheduleAtUnixSecondsBounds_ReturnsAvailable(string startTime, string endTime)
    {
        m_Handler.EnqueueJson(CreatePayload(ValidFloorsJson, startTime, endTime));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
        }
    }

    [Test]
    public async Task GetAsync_MalformedFloorLevels_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload("\"floors\":[{\"index\":12,\"levels\":null}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [TestCase("damage_rank")]
    [TestCase("defeat_rank")]
    [TestCase("take_damage_rank")]
    [TestCase("normal_skill_rank")]
    [TestCase("energy_skill_rank")]
    public async Task GetAsync_EmptySideRank_ReturnsControlledInvalidResponse(string rankName)
    {
        var payload = CreatePayload(ValidFloorsJson).Replace(
            $"\"{rankName}\":{RankEntryJson}", $"\"{rankName}\":[]");
        m_Handler.EnqueueJson(payload);

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_MissingRankList_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(ValidFloorsJson, omitDamageRank: true));

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
            Assert.That(payload!.Floors, Has.Count.EqualTo(1));
            Assert.That(payload.Floors![0].Levels, Has.Count.EqualTo(1));
            Assert.That(payload.RevealRank, Is.Empty);
            Assert.That(payload.DamageRank, Has.Count.EqualTo(1));
        }
    }

    private static string CreatePayload(string floorsMember, string startTime = "1786000000",
        string endTime = "1789000000", bool omitDamageRank = false)
    {
        var damageRank = omitDamageRank ? string.Empty : "\"damage_rank\":" + RankEntryJson + ",";

        return "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"start_time\":\"" + startTime +
               "\",\"end_time\":\"" + endTime + "\",\"reveal_rank\":[]," + damageRank +
               "\"defeat_rank\":" + RankEntryJson + ",\"take_damage_rank\":" + RankEntryJson +
               ",\"normal_skill_rank\":" + RankEntryJson + ",\"energy_skill_rank\":" + RankEntryJson +
               "," + floorsMember + "}}";
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }
}
