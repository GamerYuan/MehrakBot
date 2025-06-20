#region

using System.Net;
using System.Text.Json.Nodes;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Common;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Services;

#endregion

namespace MehrakCore.Tests.Services.Commands.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class DailyCheckInServiceTests
{
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private UserRepository m_UserRepository = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ILogger<DailyCheckInService>> m_LoggerMock = null!;
    private Mock<ILogger<GameRecordApiService>> m_GameRecordApiLoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private MongoTestHelper m_MongoTestHelper = null!;
    private Mock<HttpMessageHandler> m_MockHttpMessageHandler = null!;

    // Constants for testing
    private const string GenshinCheckInApiUrl = "https://sg-hk4e-api.hoyolab.com/event/sol/sign";
    private const string HsrCheckInApiUrl = "https://sg-public-api.hoyolab.com/event/luna/hkrpg/os/sign";
    private const string ZzzCheckInApiUrl = "https://sg-public-api.hoyolab.com/event/luna/zzz/os/sign";
    private const string Hi3CheckInApiUrl = "https://sg-public-api.hoyolab.com/event/mani/sign";

    private const string GameRecordApiUrl =
        "https://sg-public-api.hoyolab.com/event/game_record/card/wapi/getGameRecordCard";

    [SetUp]
    public void Setup()
    {
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_MongoTestHelper = new MongoTestHelper();
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);
        m_LoggerMock = new Mock<ILogger<DailyCheckInService>>();
        m_GameRecordApiLoggerMock = new Mock<ILogger<GameRecordApiService>>();
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MockHttpMessageHandler = new Mock<HttpMessageHandler>();

        // Setup HttpClient with mock handler
        var httpClient = new HttpClient(m_MockHttpMessageHandler.Object);
        m_HttpClientFactoryMock.Setup(factory => factory.CreateClient("Default")).Returns(httpClient);

        // Create GameRecordApiService with mocked dependencies
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, m_GameRecordApiLoggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    [Test]
    public async Task CheckInAsync_AllGamesSuccessful_SendsSuccessMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";
        var profile = 1u;

        var user = new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = profile, LtUid = ltuid }
            }
        };

        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(ltuid, true);

        // Setup HTTP responses for successful check-in for all games
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);
        var service = new DailyCheckInService(m_UserRepository, m_HttpClientFactoryMock.Object, m_GameRecordApiService,
            m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, user, profile, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the message contains success messages for all games
        Assert.That(responseMessage, Does.Contain("Genshin Impact: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai: Star Rail: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Zenless Zone Zero: Success").IgnoreCase);
        Assert.That(responseMessage,
            Does.Contain("Honkai Impact 3rd: Success").IgnoreCase); // Verify HTTP requests had proper headers
        VerifyHttpRequestForGame(GenshinCheckInApiUrl, ltuid, ltoken, Times.Once());
        VerifyHttpRequestForGame(HsrCheckInApiUrl, ltuid, ltoken, Times.Once());
        VerifyHttpRequestForGame(ZzzCheckInApiUrl, ltuid, ltoken, Times.Once());
        VerifyHttpRequestForGame(Hi3CheckInApiUrl, ltuid, ltoken, Times.Once());

        // Verify user profile was updated with LastCheckIn
        var updatedUser = await m_UserRepository.GetUserAsync(userId);
        Assert.That(updatedUser, Is.Not.Null);
        Assert.That(updatedUser.Profiles!.First(p => p.ProfileId == profile).LastCheckIn, Is.Not.Null);
    }

    [Test]
    public async Task CheckInAsync_MixedResults_SendsMixedResultsMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";
        var profile = 1u;

        var user = new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = profile, LtUid = ltuid }
            }
        };

        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(ltuid, true);

        // Setup HTTP responses with mixed results
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateNoAccountResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.InternalServerError, "");

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);
        var service = new DailyCheckInService(m_UserRepository, m_HttpClientFactoryMock.Object, m_GameRecordApiService,
            m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, user, profile, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the message contains the correct mixed results
        Assert.That(responseMessage, Does.Contain("Genshin Impact: Success").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai: Star Rail: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Zenless Zone Zero: No valid game account found").IgnoreCase);
        Assert.That(responseMessage,
            Does.Contain("Honkai Impact 3rd: An unknown error occurred")
                .IgnoreCase); // Verify HTTP requests had proper headers
        VerifyHttpRequestForGame(GenshinCheckInApiUrl, ltuid, ltoken, Times.Once());
        VerifyHttpRequestForGame(HsrCheckInApiUrl, ltuid, ltoken, Times.Once());
        VerifyHttpRequestForGame(ZzzCheckInApiUrl, ltuid, ltoken, Times.Once());

        // Note: User profile is not updated since not all check-ins were successful
        VerifyHttpRequestForGame(Hi3CheckInApiUrl, ltuid, ltoken, Times.Once());
    }

    [Test]
    public async Task CheckInAsync_AllAlreadyCheckedIn_SendsAlreadyCheckedInMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";
        var profile = 1u;

        var user = new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = profile, LtUid = ltuid }
            }
        };

        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(ltuid, true);

        // Setup HTTP responses for already checked in for all games
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.OK, CreateAlreadyCheckedInResponse());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);
        var service = new DailyCheckInService(m_UserRepository, m_HttpClientFactoryMock.Object, m_GameRecordApiService,
            m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, user, profile, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the message contains already checked in messages for all games        Assert.That(responseMessage, Does.Contain("Genshin Impact: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai: Star Rail: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Zenless Zone Zero: Already checked in today").IgnoreCase);
        Assert.That(responseMessage, Does.Contain("Honkai Impact 3rd: Already checked in today").IgnoreCase);

        // Note: User profile is not updated since no new check-ins were made
    }

    [Test]
    public async Task CheckInAsync_ZZZRequest_ContainsProperRpcSignHeader()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "mock-ltoken";
        var profile = 1u;

        var user = new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = profile, LtUid = ltuid }
            }
        };

        // Setup GameRecord API to return valid user data (passes guard clause)
        SetupGameRecordApiResponse(ltuid, true);

        // Setup HTTP responses
        SetupHttpResponseForUrl(GenshinCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(HsrCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(ZzzCheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());
        SetupHttpResponseForUrl(Hi3CheckInApiUrl, HttpStatusCode.OK, CreateSuccessResponse());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);
        var service = new DailyCheckInService(m_UserRepository, m_HttpClientFactoryMock.Object, m_GameRecordApiService,
            m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, user, profile, ltuid, ltoken);

        // Assert
        // Verify ZZZ request has the special X-Rpc-Signgame header
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
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "invalid-ltoken";
        var profile = 1u;

        var user = new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = profile, LtUid = ltuid }
            }
        };

        // Setup GameRecord API to return invalid data (fails guard clause)
        SetupGameRecordApiResponse(ltuid, false);

        // Note: Individual game API responses are not needed since the guard clause will fail first

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);
        var service = new DailyCheckInService(m_UserRepository, m_HttpClientFactoryMock.Object, m_GameRecordApiService,
            m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, user, profile, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the message contains the guard clause failure message
        Assert.That(responseMessage,
            Does.Contain("Invalid UID or Cookies. Please re-authenticate your profile").IgnoreCase);
    }

    [Test]
    public async Task CheckInAsync_GameRecordApiReturnsNull_SendsInvalidCookiesMessage()
    {
        // Arrange
        var userId = 123456789UL;
        var ltuid = 987654321UL;
        var ltoken = "invalid-ltoken";
        var profile = 1u;

        var user = new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = profile, LtUid = ltuid }
            }
        };

        // Setup GameRecord API to return null (fails guard clause)
        SetupGameRecordApiResponse(ltuid, false);

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(userId);
        var context = CreateMockInteractionContext(interaction);
        var service = new DailyCheckInService(m_UserRepository, m_HttpClientFactoryMock.Object, m_GameRecordApiService,
            m_LoggerMock.Object);

        // Act
        await service.CheckInAsync(context, user, profile, ltuid, ltoken);

        // Assert
        var responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the message contains the guard clause failure message
        Assert.That(responseMessage,
            Does.Contain("Invalid UID or Cookies. Please re-authenticate your profile").IgnoreCase);

        // Verify that individual game check-in APIs were not called since guard clause failed
        m_MockHttpMessageHandler.Protected()
            .Verify("SendAsync", Times.Never(), ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString() == GenshinCheckInApiUrl ||
                    req.RequestUri!.ToString() == HsrCheckInApiUrl ||
                    req.RequestUri!.ToString() == ZzzCheckInApiUrl ||
                    req.RequestUri!.ToString() == Hi3CheckInApiUrl),
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

    private static string CreateSuccessResponse()
    {
        var jsonResponse = new JsonObject
        {
            ["retcode"] = 0,
            ["message"] = "OK",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateAlreadyCheckedInResponse()
    {
        var jsonResponse = new JsonObject
        {
            ["retcode"] = -5003,
            ["message"] = "Already checked in",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateNoAccountResponse()
    {
        var jsonResponse = new JsonObject
        {
            ["retcode"] = -10002,
            ["message"] = "No valid game account found",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private static string CreateInvalidCookiesResponse()
    {
        var jsonResponse = new JsonObject
        {
            ["retcode"] = 10001,
            ["message"] = "Invalid cookies",
            ["data"] = new JsonObject()
        };
        return jsonResponse.ToJsonString();
    }

    private void SetupGameRecordApiResponse(ulong ltuid, bool isValid)
    {
        var gameRecordUrl = $"{GameRecordApiUrl}?uid={ltuid}";

        // Setup successful GameRecord API response
        SetupHttpResponseForUrl(gameRecordUrl, HttpStatusCode.OK,
            // Setup failed GameRecord API response (invalid cookies)
            isValid ? CreateValidGameRecordResponse() : CreateInvalidGameRecordResponse());
    }

    private static string CreateValidGameRecordResponse()
    {
        var jsonResponse = new JsonObject
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
                        ["game_name"] = "Genshin Impact",
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
        var jsonResponse = new JsonObject
        {
            ["retcode"] = 10001,
            ["message"] = "Invalid cookies",
            ["data"] = null
        };
        return jsonResponse.ToJsonString();
    }

    private static IInteractionContext CreateMockInteractionContext(SlashCommandInteraction interaction)
    {
        var contextMock = new Mock<IInteractionContext>();
        contextMock.SetupGet(x => x.Interaction).Returns(interaction);
        return contextMock.Object;
    }
}
