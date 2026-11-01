using System.Net;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.GameApi.Tests.Genshin;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinTheaterApiServiceTests
{
    private const string TheaterRecordJson = """
        {
          "detail": {
            "rounds_data": [],
            "detail_stat": {"get_medal_round_list": []},
            "backup_avatars": [],
            "fight_statisic": {
              "max_defeat_avatar": {"value": ""},
              "max_damage_avatar": {"value": ""},
              "max_take_damage_avatar": {"value": ""},
              "total_coin_consumed": {"value": ""},
              "shortest_avatar_list": []
            }
          },
          "stat": {"get_medal_round_list": []},
          "schedule": {
            "start_time": "1786000000",
            "end_time": "1789000000",
            "start_date_time": {},
            "end_date_time": {}
          },
          "has_data": true,
          "has_detail_data": true
        }
        """;

    private FakeHttpMessageHandler m_Handler = null!;
    private ServiceProvider m_Provider = null!;
    private IApiService<GenshinTheaterResponseData, BaseHoYoApiContext> m_Service = null!;

    [SetUp]
    public void SetUp()
    {
        m_Handler = new FakeHttpMessageHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(m_Handler.ToHttpClientFactory());
        services.AddGameApiServices();

        m_Provider = services.BuildServiceProvider();
        m_Service = m_Provider.GetRequiredService<IApiService<GenshinTheaterResponseData, BaseHoYoApiContext>>();
    }

    [TearDown]
    public void TearDown()
    {
        m_Provider.Dispose();
    }

    [Test]
    public async Task GetAsync_UnlockedEmptyTheaterDataWithTarotState_ReturnsFullEnvelope()
    {
        m_Handler.EnqueueJson(CreatePayload("[]", tarotCardStateJson: CreateTarotCardStateJson()));

        var result = await m_Service.GetAsync(CreateContext());

        Assert.That(result.IsSuccess, Is.True);
        var response = result.Data;
        Assert.That(response, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.IsUnlock, Is.True);
            Assert.That(response.Data, Is.Empty);
            Assert.That(response.TarotCardState, Is.Not.Null);
            Assert.That(response.TarotCardState!.TotalNum, Is.EqualTo(22));
            Assert.That(response.TarotCardState.CurrNum, Is.EqualTo(21));
            Assert.That(response.TarotCardState.List, Has.Count.EqualTo(22));

            var lockedCard = response.TarotCardState.List[0];
            Assert.That(lockedCard.Name, Is.EqualTo("Card 1"));
            Assert.That(lockedCard.Icon, Is.Empty);
            Assert.That(lockedCard.IsUnlock, Is.False);
            Assert.That(lockedCard.UnlockNum, Is.Zero);

            var unlockedCard = response.TarotCardState.List[1];
            Assert.That(unlockedCard.Icon, Is.EqualTo("https://example.invalid/tarot/2.png"));
            Assert.That(unlockedCard.IsUnlock, Is.True);
            Assert.That(unlockedCard.UnlockNum, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task GetAsync_TheaterRecordAndTarotState_ReturnsBothEnvelopeFields()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{TheaterRecordJson}]", tarotCardStateJson: CreateTarotCardStateJson()));

        var result = await m_Service.GetAsync(CreateContext());

        Assert.That(result.IsSuccess, Is.True);
        var response = result.Data;
        Assert.That(response, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.Data, Has.Count.EqualTo(1));
            Assert.That(response.Data[0].HasDetailData, Is.True);
            Assert.That(response.TarotCardState, Is.Not.Null);
            Assert.That(response.TarotCardState!.List, Has.Count.EqualTo(22));
        }
    }

    [Test]
    public async Task GetAsync_LegacyResponseWithoutTarotState_ReturnsTheaterRecord()
    {
        m_Handler.EnqueueJson(CreatePayload($"[{TheaterRecordJson}]"));

        var result = await m_Service.GetAsync(CreateContext());

        Assert.That(result.IsSuccess, Is.True);
        var response = result.Data;
        Assert.That(response, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.TarotCardState, Is.Null);
            Assert.That(response.Data, Has.Count.EqualTo(1));
            Assert.That(response.Data[0].HasDetailData, Is.True);
            Assert.That(response.Data[0].Schedule.StartTime, Is.EqualTo("1786000000"));
        }
    }

    [Test]
    public async Task GetAsync_UnlockedEmptyTheaterDataWithoutTarotState_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson(CreatePayload("[]"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
            Assert.That(result.ErrorMessage, Does.Contain("No Imaginarium Theater data found"));
        }
    }

    [Test]
    public async Task GetAsync_NotUnlocked_ReturnsUnauthorized()
    {
        m_Handler.EnqueueJson(CreatePayload("[]", isUnlock: false, tarotCardStateJson: CreateTarotCardStateJson()));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Contain("not unlocked"));
        }
    }

    [TestCase(10001, StatusCode.Unauthorized)]
    [TestCase(12345, StatusCode.ExternalServerError)]
    public async Task GetAsync_ErrorRetcode_ReturnsExistingMappedFailure(int retcode, StatusCode expectedStatus)
    {
        m_Handler.EnqueueJson(CreatePayload("[]", retcode: retcode));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(expectedStatus));
        }
    }

    [Test]
    public async Task GetAsync_NonSuccessStatusCode_ReturnsExternalServerError()
    {
        m_Handler.EnqueueJson("{}", HttpStatusCode.InternalServerError);

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.ExternalServerError));
        }
    }

    [Test]
    public async Task GetAsync_InvalidRegionOrUid_ReturnsBadParameterWithoutSendingRequest()
    {
        var context = new BaseHoYoApiContext(1, 100, "valid-token", null, "os_asia");

        var result = await m_Service.GetAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.BadParameter));
            Assert.That(m_Handler.RequestCount, Is.Zero);
        }
    }

    [Test]
    public async Task GetAsync_MalformedCredential_RejectsBeforeSendingRequestAndDoesNotEchoToken()
    {
        const string malformedToken = "bad token";
        var context = new BaseHoYoApiContext(1, 100, malformedToken, "900000001", "os_asia");

        var result = await m_Service.GetAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Not.Contain(malformedToken));
            Assert.That(m_Handler.RequestCount, Is.Zero);
        }
    }

    [Test]
    public async Task GetAsync_CancelledRequest_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        m_Handler.Enqueue(() =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var result = await m_Service.GetAsync(CreateContext(), cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Cancelled));
            Assert.That(m_Handler.RequestCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetAsync_RequestTimesOut_ReturnsTimeout()
    {
        m_Handler.Enqueue(() => throw new TaskCanceledException("simulated timeout"));

        var result = await m_Service.GetAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCode.Timeout));
        }
    }

    private static BaseHoYoApiContext CreateContext()
    {
        return new BaseHoYoApiContext(1, 100, "valid-token", "900000001", "os_asia");
    }

    private static string CreatePayload(string dataJson, bool isUnlock = true, string? tarotCardStateJson = null,
        int retcode = 0)
    {
        var tarotCardState = tarotCardStateJson is null
            ? string.Empty
            : $",\"tarot_card_state\":{tarotCardStateJson}";

        return $"{{\"retcode\":{retcode},\"message\":\"OK\",\"data\":{{\"data\":{dataJson}," +
               $"\"is_unlock\":{(isUnlock ? "true" : "false")}{tarotCardState}}}}}";
    }

    private static string CreateTarotCardStateJson(int cardCount = 22)
    {
        var cards = Enumerable.Range(1, cardCount).Select(index =>
        {
            var isUnlock = index > 1;
            var icon = index == 1 ? string.Empty : $"https://example.invalid/tarot/{index}.png";
            var unlockNum = index == 2 ? 3 : 0;

            return $"{{\"icon\":\"{icon}\",\"name\":\"Card {index}\"," +
                   $"\"is_unlock\":{(isUnlock ? "true" : "false")},\"unlock_num\":{unlockNum}}}";
        });

        return $"{{\"total_num\":22,\"curr_num\":21,\"list\":[{string.Join(",", cards)}]}}";
    }
}
