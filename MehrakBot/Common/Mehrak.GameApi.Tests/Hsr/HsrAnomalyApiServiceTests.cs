#region

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
public class HsrAnomalyApiServiceTests
{
    private const string RecordJson = """
        {
          "group": {
            "group_id": 1,
            "begin_time": {"year": 2026, "month": 1, "day": 1, "hour": 0, "minute": 0},
            "end_time": {"year": 2026, "month": 2, "day": 1, "hour": 0, "minute": 0},
            "name_mi18n": "Group",
            "theme_pic_path": "theme.png",
            "game_version": "3.0"
          },
          "boss_info": {"name_mi18n": "Boss", "icon": "boss.png"},
          "mob_infos": [],
          "boss_record": null,
          "mob_records": [],
          "has_challenge_record": false,
          "battle_num": 0,
          "boss_stars": 0,
          "mob_stars": 0
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private HsrAnomalyApiService m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();
        m_Service = new HsrAnomalyApiService(m_Handler.ToHttpClientFactory(),
            Mock.Of<ILogger<HsrAnomalyApiService>>());
    }

    [Test]
    public async Task GetAsync_NullChallengeRecords_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload("null"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.RequestUri, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetAsync_EmptyChallengeRecords_ReturnsEmptyOutcome()
    {
        m_Handler.EnqueueJson(CreatePayload("[]"));

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
    public async Task GetAsync_NullBestRecord_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(
            """{"retcode":0,"message":"OK","data":{"challenge_peak_records":[{"group":{"group_id":1,"begin_time":{"year":2026,"month":1,"day":1,"hour":0,"minute":0},"end_time":{"year":2026,"month":2,"day":1,"hour":0,"minute":0},"name_mi18n":"G","theme_pic_path":"t.png","game_version":"3.0"},"boss_info":{"name_mi18n":"B","icon":"i"},"mob_infos":[],"boss_record":null,"mob_records":[],"has_challenge_record":false,"battle_num":0,"boss_stars":0,"mob_stars":0}],"challenge_peak_best_record_brief":null}}""");

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullMobAvatarEntry_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(
            "[{\"group\":{\"group_id\":1,\"begin_time\":{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0},\"end_time\":{\"year\":2026,\"month\":2,\"day\":1,\"hour\":0,\"minute\":0},\"name_mi18n\":\"G\",\"theme_pic_path\":\"t.png\",\"game_version\":\"3.0\"},\"boss_info\":{\"name_mi18n\":\"B\",\"icon\":\"i\"},\"mob_infos\":[],\"boss_record\":null,\"mob_records\":[{\"avatars\":[null]}],\"has_challenge_record\":false,\"battle_num\":0,\"boss_stars\":0,\"mob_stars\":0}]"));

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
        m_Handler.EnqueueJson(CreatePayload(
            $"[{{\"group\":{{\"group_id\":1,\"begin_time\":{{\"year\":{year},\"month\":{month},\"day\":{day},\"hour\":0,\"minute\":0}},\"end_time\":{{\"year\":2026,\"month\":2,\"day\":1,\"hour\":0,\"minute\":0}},\"name_mi18n\":\"G\",\"theme_pic_path\":\"t.png\",\"game_version\":\"3.0\"}},\"boss_info\":{{\"name_mi18n\":\"B\",\"icon\":\"i\"}},\"mob_infos\":[],\"boss_record\":null,\"mob_records\":[],\"has_challenge_record\":false,\"battle_num\":0,\"boss_stars\":0,\"mob_stars\":0}}]"));

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
        m_Handler.EnqueueJson(CreatePayload(
            $"[{{\"group\":{{\"group_id\":1,\"begin_time\":{{\"year\":2026,\"month\":1,\"day\":1,\"hour\":0,\"minute\":0}},\"end_time\":{{\"year\":{year},\"month\":{month},\"day\":{day},\"hour\":0,\"minute\":0}},\"name_mi18n\":\"G\",\"theme_pic_path\":\"t.png\",\"game_version\":\"3.0\"}},\"boss_info\":{{\"name_mi18n\":\"B\",\"icon\":\"i\"}},\"mob_infos\":[],\"boss_record\":null,\"mob_records\":[],\"has_challenge_record\":false,\"battle_num\":0,\"boss_stars\":0,\"mob_stars\":0}}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }
    [Test]
    public async Task GetAsync_NullBossInfo_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(bossInfoJson: "null")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullBossInfoName_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload(
            $"[{CreateRecordJson(bossInfoJson: "{\"name_mi18n\":null,\"icon\":\"i\"}")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullBossRecordBuff_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(
            bossRecordJson: "{\"avatars\":[],\"buff\":null,\"challenge_peak_rank_icon_type\":\"x\",\"challenge_peak_rank_icon\":\"i\"}")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullBossRecordAvatarEntry_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(
            bossRecordJson: "{\"avatars\":[null],\"buff\":{\"id\":1,\"name_mi18n\":\"B\",\"desc_mi18n\":\"D\",\"icon\":\"i\"},\"challenge_peak_rank_icon_type\":\"x\",\"challenge_peak_rank_icon\":\"i\"}")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_AvailableWithBossRecord_ReturnsPayload()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(
            bossRecordJson: "{\"avatars\":[],\"buff\":{\"id\":1,\"name_mi18n\":\"B\",\"desc_mi18n\":\"D\",\"icon\":\"i\"},\"challenge_peak_rank_icon_type\":\"x\",\"challenge_peak_rank_icon\":\"i\"}")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
            Assert.That(result.Data.Payload!.ChallengeRecords[0].BossRecord, Is.Not.Null);
        }
    }
    [Test]
    public async Task GetAsync_NullMobInfo_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(mobInfoJson: "null")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_NullMobInfoEntry_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(mobInfoJson: "[null]")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_MobRecordWithoutMatchingMobInfo_ReturnsControlledInvalidResponse()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(
            mobInfoJson: "[{\"maze_id\":1,\"name\":\"Mob\",\"monster_name\":\"Monster\"}]",
            mobRecordsJson: "[{\"maze_id\":2,\"avatars\":[]}]")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_AvailableWithMatchingMobInfo_ReturnsPayload()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{CreateRecordJson(
            mobInfoJson: "[{\"maze_id\":1,\"name\":\"Mob\",\"monster_name\":\"Monster\"}]",
            mobRecordsJson: "[{\"maze_id\":1,\"avatars\":[]}]")}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
        }
    }
    [Test]
    public async Task GetAsync_Available_ReturnsPayload()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{RecordJson}]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.Not.Null);
            Assert.That(result.Data!.IsAvailable, Is.True);
            var payload = result.Data.Payload;
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.ChallengeRecords, Has.Count.EqualTo(1));
            Assert.That(payload.BestRecord, Is.Not.Null);
        }
    }

    private static string CreateRecordJson(
        string bossInfoJson = "{\"name_mi18n\":\"Boss\",\"icon\":\"boss.png\"}",
        string? bossRecordJson = null,
        string mobInfoJson = "[]",
        string mobRecordsJson = "[]")
    {
        var bossRecord = bossRecordJson ?? "null";

        return $$"""
            {
              "group": {
                "group_id": 1,
                "begin_time": {"year":2026,"month":1,"day":1,"hour":0,"minute":0},
                "end_time": {"year":2026,"month":2,"day":1,"hour":0,"minute":0},
                "name_mi18n": "Group",
                "theme_pic_path": "theme.png",
                "game_version": "3.0"
              },
              "boss_info": {{bossInfoJson}},
              "mob_infos": {{mobInfoJson}},
              "boss_record": {{bossRecord}},
              "mob_records": {{mobRecordsJson}},
              "has_challenge_record": false,
              "battle_num": 0,
              "boss_stars": 0,
              "mob_stars": 0
            }
            """;
    }

    private static string CreatePayload(string recordsJson)
    {
        return "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"challenge_peak_records\":" + recordsJson +
               ",\"challenge_peak_best_record_brief\":{\"total_battle_num\":0,\"boss_stars\":0," +
               "\"mob_stars\":0,\"challenge_peak_rank_icon_type\":0,\"challenge_peak_rank_icon\":\"icon.png\"}}}";
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }
}
