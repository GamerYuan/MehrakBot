#region

using System.Net;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Common;
using Mehrak.GameApi;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

#endregion

namespace MehrakCore.Tests.Services.Commands.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class DailyCheckInServiceTests
{
    private const ulong TestLtuid = 987654321UL;
    private const string TestLtoken = "test_ltoken_value";

    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ILogger<DailyCheckInService>> m_LoggerMock = null!;
    private Mock<ILogger<GameRecordApiService>> m_GameRecordApiLoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<HttpMessageHandler> m_MockHttpMessageHandler = null!;

    private DailyCheckInService m_Service = null!;

    // Constants for testing
    private static readonly string GenshinCheckInApiUrl = $"{HoYoLabDomains.GenshinApi}/event/sol/sign";
    private static readonly string HsrCheckInApiUrl = $"{HoYoLabDomains.PublicApi}/event/luna/hkrpg/os/sign";
    private static readonly string ZzzCheckInApiUrl = $"{HoYoLabDomains.PublicApi}/event/luna/zzz/os/sign";
    private static readonly string Hi3CheckInApiUrl = $"{HoYoLabDomains.PublicApi}/event/mani/sign";
    private static readonly string ToTCheckInApiUrl = $"{HoYoLabDomains.PublicApi}/event/luna/nxx/os/sign";

    private static readonly string GameRecordApiUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/card/wapi/getGameRecordCard";

    [SetUp]
    public void Setup()
    {
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_LoggerMock = new Mock<ILogger<DailyCheckInService>>();
        m_GameRecordApiLoggerMock = new Mock<ILogger<GameRecordApiService>>();
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MockHttpMessageHandler = new Mock<HttpMessageHandler>();

        // Setup HttpClient with mock handler
        HttpClient httpClient = new(m_MockHttpMessageHandler.Object);
        m_HttpClientFactoryMock.Setup(factory => factory.CreateClient("Default")).Returns(httpClient);

        // Create GameRecordApiService with mocked dependencies
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, m_GameRecordApiLoggerMock.Object);

        m_Service = new(m_HttpClientFactoryMock.Object, m_GameRecordApiService, m_LoggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
    }

    [Test]
    public async Task CheckInAsync_AllGamesSuccessful_SendsSuccessMessage()
    {
        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(TestLtuid, true);

        // Setup HTTP responses for successful check-in for all games
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(ToTCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());

        // Act
        Result<(bool, string)> response = await m_Service.CheckInAsync(TestLtuid, TestLtoken);

        // Assert
        Assert.That(response.IsSuccess, Is.True);
        string? responseMessage = response.Data.Item2;

        // Verify the message contains success messages for all games
        Assert.That(responseMessage, Does.Contain("Genshin Impact: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai: Star Rail: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Zenless Zone Zero: Success").IgnoreCase);
        Assert.That(responseMessage,
            Does.Contain("Honkai Impact 3rd: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Tears of Themis: Success").IgnoreCase);

        VerifyHttpRequestForGame(GenshinCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(HsrCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(ZzzCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(Hi3CheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(ToTCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
    }

    [Test]
    public async Task CheckInAsync_MixedResults_SendsMixedResultsMessage()
    {
        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(TestLtuid, true);

        // Setup HTTP responses with mixed results
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateNoAccountResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.InternalServerError, "");
        SetupHttpResponseForUrl(ToTCheckInApiUrl, HttpStatusCode.InternalServerError, "");

        // Act
        Result<(bool, string)> response = await m_Service.CheckInAsync(TestLtuid, TestLtoken);

        // Assert
        Assert.That(response.IsSuccess, Is.True);
        string? responseMessage = response.Data.Item2;

        // Verify the message contains the correct mixed results
        Assert.That(responseMessage, Does.Contain("Genshin Impact: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai: Star Rail: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Zenless Zone Zero: No valid game account found").IgnoreCase);
        Assert.That(responseMessage,
            Does.Contain("Honkai Impact 3rd: An unknown error occurred")
                .IgnoreCase);
        Assert.That(responseMessage,
            Does.Contain("Tears of Themis: An unknown error occurred")
                .IgnoreCase);

        VerifyHttpRequestForGame(GenshinCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(HsrCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(ZzzCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(Hi3CheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
        VerifyHttpRequestForGame(ToTCheckInApiUrl, TestLtuid, TestLtoken, Times.Once());
    }

    [Test]
    public async Task CheckInAsync_AllAlreadyCheckedIn_SendsAlreadyCheckedInMessage()
    {
        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(TestLtuid, true);

        // Setup HTTP responses for already checked in for all games
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(ToTCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());

        // Act
        Result<(bool, string)> response = await m_Service.CheckInAsync(TestLtuid, TestLtoken);

        // Assert
        Assert.That(response.IsSuccess, Is.True);
        string? responseMessage = response.Data.Item2;

        // Verify the message contains already checked in messages for all games
        Assert.That(responseMessage, Does.Contain("Genshin Impact: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai: Star Rail: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Zenless Zone Zero: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai Impact 3rd: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Tears of Themis: Already checked in today").IgnoreCase);

        // Note: User profile is not updated since no new check-ins were made
    }

    [Test]
    public async Task CheckInAsync_ZZZRequest_ContainsProperRpcSignHeader()
    {
        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(TestLtuid, true);

        // Setup HTTP responses
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(ToTCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());

        // Act
        await m_Service.CheckInAsync(TestLtuid, TestLtoken);

        // Assert Verify ZZZ request has the special X-Rpc-Signgame header
        m_MockHttpMessageHandler.Protected()
            .Verify("SendAsync", Times.AtLeastOnce(), ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString() == ZzzCheckInApiUrl &&
                    req.Headers.Contains("X-Rpc-Signgame") &&
                    req.Headers.GetValues("X-Rpc-Signgame").First() == "zzz"),
                ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task CheckInAsync_InvalidCookies_SendsInvalidCookiesMessage()
    {
        // Setup GameRecord API to return invalid data (fails guard clause)
        SetupGameRecordApiResponse(TestLtuid, false);

        // Note: Individual game API responses are not needed since the guard
        // clause will fail first

        // Act
        Result<(bool, string)> response = await m_Service.CheckInAsync(TestLtuid, TestLtoken);

        // Assert
        Assert.That(response.IsSuccess, Is.False);
        string? responseMessage = response.ErrorMessage;

        // Verify the message contains the guard clause failure message
        Assert.That(responseMessage,
            Does.Contain("Invalid UID or Cookies. Please re-authenticate your profile").IgnoreCase);
    }

    [Test]
    public async Task CheckInAsync_GameRecordApiReturnsNull_SendsInvalidCookiesMessage()
    {
        // Setup GameRecord API to return null (fails guard clause)
        SetupGameRecordApiResponse(TestLtuid, false);

        // Act
        Result<(bool, string)> response = await m_Service.CheckInAsync(TestLtuid, TestLtoken);

        // Assert
        Assert.That(response.IsSuccess, Is.False);
        string? responseMessage = response.ErrorMessage;

        // Verify the message contains the guard clause failure message
        Assert.That(responseMessage,
            Does.Contain("Invalid UID or Cookies. Please re-authenticate your profile").IgnoreCase);

        // Verify that individual game check-in APIs were not called since guard
        // clause failed
        m_MockHttpMessageHandler.Protected()
            .Verify("SendAsync", Times.Never(), ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString() == GenshinCheckInApiUrl ||
                    req.RequestUri!.ToString() == HsrCheckInApiUrl ||
                    req.RequestUri!.ToString() == ZzzCheckInApiUrl ||
                    req.RequestUri!.ToString() == Hi3CheckInApiUrl ||
                    req.RequestUri!.ToString() == ToTCheckInApiUrl),
                ItExpr.IsAny<CancellationToken>());
    }

    private void SetupHttpResponseForUrl(string url, HttpStatusCode statusCode, string content)
    {
        m_MockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
    }

    private void VerifyHttpRequestForGame(string url, ulong ltuid, string ltoken, Times times)
    {
        m_MockHttpMessageHandler.Protected()
            .Verify("SendAsync", times, ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString() == url &&
                    req.Headers.Contains("Cookie") &&
                    req.Headers.GetValues("Cookie").Any(v =>
                        v.Contains($"ltuid_v2={ltuid}") && v.Contains($"ltoken_v2={ltoken}"))),
                ItExpr.IsAny<CancellationToken>());
    }

    private void SetupGameRecordApiResponse(ulong ltuid, bool isValid)
    {
        string gameRecordUrl = $"{GameRecordApiUrl}?uid={ltuid}";

        // Setup successful GameRecord API response
        SetupHttpResponseForUrl(gameRecordUrl, HttpStatusCode.OK,
            // Setup failed GameRecord API response (invalid cookies)
            isValid ? CreateValidGameRecordResponse() : CreateInvalidGameRecordResponse());
    }

    private static string CreateSuccessResponse()
    {
        JsonObject jsonResponse = new()
        {
            ["retcode"] = 0,
            ["message"] = "OK",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateAlreadyCheckedInResponse()
    {
        JsonObject jsonResponse = new()
        {
            ["retcode"] = -5003,
            ["message"] = "Already checked in",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateNoAccountResponse()
    {
        JsonObject jsonResponse = new()
        {
            ["retcode"] = -10002,
            ["message"] = "No valid game account found",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateValidGameRecordResponse()
    {
        JsonObject jsonResponse = new()
        {
            ["retcode"] = 0,
            ["message"] = "OK",
            ["data"] = new JsonObject
            {
                ["list"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["game_id"] = 2,
                        ["game_name"] = "hk4e",
                        ["game_role_id"] = "123456789",
                        ["nickname"] = "TestPlayer",
                        ["region"] = "os_usa",
                        ["level"] = 60
                    }
                }
            }
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateInvalidGameRecordResponse()
    {
        JsonObject jsonResponse = new()
        {
            ["retcode"] = 10001,
            ["message"] = "Invalid cookies",
            ["data"] = null
        };
        return jsonResponse.ToJsonString();
    }
}