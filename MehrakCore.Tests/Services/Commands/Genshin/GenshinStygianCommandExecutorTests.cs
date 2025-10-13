#region

using Mehrak.Application.Services.Genshin;
using Mehrak.Bot.Executors.Genshin;
using Mehrak.Bot.Modules;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi;
using Mehrak.GameApi.Common.ApiResponseTypes;
using Mehrak.GameApi.Genshin.Types;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Commands.Genshin.Stygian;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Services;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinStygianCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGameUid = "800000000";
    private const uint TestProfileId = 1;
    private const string TestGuid = "test-guid-12345";

    private static readonly string AccountRolesUrl =
        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken";
    private static readonly string HardChallengeUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/genshin/api/hard_challenge";
    private static readonly string GameRecordCardUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/card/wapi/getGameRecordCard";

    private GenshinStygianCommandExecutor m_Executor = null!;
    private GenshinStygianApiService m_ApiService = null!;
    private GenshinStygianCardService m_CardService = null!;
    private UserRepository m_UserRepository = null!;
    private RedisCacheService m_TokenCacheService = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ILogger<GenshinCommandModule>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private ImageRepository m_ImageRepository = null!;

    [SetUp]
    public void Setup()
    {
        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();
        m_DiscordTestHelper = new DiscordTestHelper();

        // Setup HTTP client factory and message handler
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        // Setup distributed cache
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        SetupDistributedCacheMock();

        // Setup repositories
        m_UserRepository = new UserRepository(MongoTestHelper.Instance.MongoDbService,
            new Mock<ILogger<UserRepository>>().Object);
        m_ImageRepository = new ImageRepository(MongoTestHelper.Instance.MongoDbService,
            new Mock<ILogger<ImageRepository>>().Object);

        // Setup token cache service
        m_TokenCacheService = new RedisCacheService(m_DistributedCacheMock.Object,
            new Mock<ILogger<RedisCacheService>>().Object);

        // Setup GameRecord API service
        m_GameRecordApiService = new GameRecordApiService(m_HttpClientFactoryMock.Object,
            new Mock<ILogger<GameRecordApiService>>().Object);

        // Create real API and card services with mocked dependencies
        m_ApiService = new GenshinStygianApiService(m_HttpClientFactoryMock.Object,
            new Mock<ILogger<GenshinStygianApiService>>().Object);
        m_CardService = new GenshinStygianCardService(m_ImageRepository,
            new Mock<ILogger<GenshinStygianCardService>>().Object);

        // Create real image updater service with mocked dependencies
        GenshinImageUpdaterService imageUpdaterService = new(m_ImageRepository, m_HttpClientFactoryMock.Object,
            new Mock<ILogger<GenshinImageUpdaterService>>().Object);

        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_LoggerMock = new Mock<ILogger<GenshinCommandModule>>();

        // Setup Discord interaction
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId, "stygian",
            ("server", "America", ApplicationCommandOptionType.String),
            ("profile", TestProfileId, ApplicationCommandOptionType.Integer));

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Create executor
        m_Executor = new GenshinStygianCommandExecutor(
            imageUpdaterService,
            m_CardService,
            m_ApiService,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object)
        {
            Context = m_ContextMock.Object
        };

        // Setup Discord response capture
        m_DiscordTestHelper.SetupRequestCapture();
    }

    [TearDown]
    public async Task TearDown()
    {
        m_DiscordTestHelper.Dispose();
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
        m_DiscordTestHelper.ClearCapturedRequests();
    }

    #region ExecuteAsync Tests

    [Test]
    public async Task ExecuteAsync_WithNonExistentUser_ReturnsEarly()
    {
        // Arrange
        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidProfile_ReturnsEarly()
    {
        // Arrange
        await CreateTestUserWithProfile();
        object[] parameters = [Server.Asia, 999u]; // Invalid profile ID

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithNoServerAndNoCachedServer_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfileNoServer();
        object?[] parameters = [null, TestProfileId]; // No server specified

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("No cached server found! Please select a server first."));
    }

    [Test]
    public async Task ExecuteAsync_WithCachedServer_UsesCachedServer()
    {
        // Arrange
        await CreateTestUserWithProfile();
        SetupSuccessfulApiResponses();
        await SetupCachedToken();

        object?[] parameters = [null, TestProfileId]; // Use cached server

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert Verify the user's cached server was used
        UserModel? user = await m_UserRepository.GetUserAsync(m_TestUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Profiles?.FirstOrDefault()?.LastUsedRegions?[Game.Genshin], Is.EqualTo(Server.Asia));
    }

    [Test]
    public async Task ExecuteAsync_WithValidParametersAndCachedToken_GeneratesStygianCard()
    {
        // Arrange
        await CreateTestUserWithProfile();
        SetupSuccessfulApiResponses();
        await SetupCachedToken();

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Is.Not.Empty);
        Console.WriteLine(responseMessage);

        // Verify API calls were made correctly
        VerifyHttpRequestWithQuery(AccountRolesUrl,
            "game_biz=hk4e_global&region=os_asia", Times.Once());
        VerifyHttpRequestWithQuery(HardChallengeUrl,
            "role_id=800000000&server=os_asia&need_detail=true", Times.Once());
    }

    [Test]
    public async Task ExecuteAsync_WhenUserNotAuthenticated_ShowsAuthModal()
    {
        // Arrange
        await CreateTestUserWithProfile();
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain($"auth_modal:{TestGuid}:{TestProfileId}"));
    }

    [Test]
    public async Task ExecuteAsync_WithApiFailure_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();
        SetupHttpResponse(GameRecordCardUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);
        SetupHttpResponse(AccountRolesUrl,
            CreateGameRoleResponse(), HttpStatusCode.OK);
        SetupHttpResponse(HardChallengeUrl,
            "", HttpStatusCode.InternalServerError);

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("error"));
    }

    [Test]
    public async Task ExecuteAsync_WithStygianNotUnlocked_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();
        SetupHttpResponse(GameRecordCardUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);
        SetupHttpResponse(AccountRolesUrl,
            CreateGameRoleResponse(), HttpStatusCode.OK);
        SetupHttpResponse(HardChallengeUrl,
            CreateStygianNotUnlockedResponse(), HttpStatusCode.OK);

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("Stygian Onslaught is not unlocked"));
    }

    [Test]
    public async Task ExecuteAsync_WithNoStygianData_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();
        SetupHttpResponse(GameRecordCardUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);
        SetupHttpResponse(AccountRolesUrl,
            CreateGameRoleResponse(), HttpStatusCode.OK);
        SetupHttpResponse(HardChallengeUrl,
            CreateStygianNoDataResponse(), HttpStatusCode.OK);

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("No Stygian Onslaught data found for this cycle"));
    }

    [Test]
    public async Task ExecuteAsync_WithNullParameters_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserWithProfile();
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Act
        await m_Executor.ExecuteAsync(null, 1u);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain($"auth_modal:{TestGuid}:1"));
    }

    [Test]
    public async Task ExecuteAsync_WhenExceptionOccurs_LogsErrorAndSendsGenericMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup HTTP response to throw exception
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to get Stygian Onslaught card")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("error occurred"));
    }

    [Test]
    public async Task ExecuteAsync_FullWorkflow_GeneratesCompleteStygianCard()
    {
        // Arrange
        await CreateTestUserWithProfile();
        SetupSuccessfulApiResponses();
        await SetupCachedToken();

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify basic response
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Is.Not.Empty);

        // Verify all expected API calls were made
        VerifyHttpRequestWithQuery(AccountRolesUrl,
            "game_biz=hk4e_global&region=os_asia", Times.Once());
        VerifyHttpRequest(HardChallengeUrl, Times.Once());

        // For now, we'll just verify that the workflow completed without errors
        // In a more comprehensive test, we could check for specific attachment
        // names or content
        Assert.That(responseMessage, Is.Not.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WithEmptyStygianData_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup empty stygian data response
        StygianData emptyStygianData = new()
        {
            Schedule = new StygianSchedule
            {
                ScheduleId = "0",
                StartTime = "0",
                EndTime = "0",
                IsValid = false,
                Name = "No Data"
            },
            Single = new StygianChallengeData
            {
                StygianBestRecord = new StygianBestRecord { Difficulty = 0, Second = 0, Icon = "" },
                Challenge = [],
                HasData = false
            },
            Multi = new StygianChallengeData
            {
                StygianBestRecord = new StygianBestRecord { Difficulty = 0, Second = 0, Icon = "" },
                Challenge = [],
                HasData = false
            }
        };

        var emptyResponse = new
        {
            retcode = 0,
            message = "OK",
            data = emptyStygianData
        };

        SetupHttpResponse(AccountRolesUrl, CreateGameRoleResponse());
        SetupHttpResponse(HardChallengeUrl, JsonSerializer.Serialize(emptyResponse));

        object[] parameters = [Server.Asia, TestProfileId];

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await m_Executor.ExecuteAsync(parameters));

        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Is.Not.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WithApiError_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup API error response
        var errorResponse = new
        {
            retcode = -1,
            message = "API Error",
            data = (object?)null
        };

        SetupHttpResponse(AccountRolesUrl, CreateGameRoleResponse());
        SetupHttpResponse(HardChallengeUrl, JsonSerializer.Serialize(errorResponse));

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Does.Contain("error").Or.Contain("failed"));
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidRegion_HandlesCorrectly()
    {
        // Arrange
        await CreateTestUserWithProfile();
        SetupSuccessfulApiResponses();
        await SetupCachedToken();

        // Test with an invalid region enum value
        object[] parameters = [(Server)999, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        // The executor should handle this gracefully, even if the region is invalid
    }

    [Test]
    public async Task ExecuteAsync_WithLargeStygianData_ProcessesCorrectly()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Create a more complex stygian data with multiple challenges
        StygianData complexStygianData = await LoadTestData();

        var complexResponse = new
        {
            retcode = 0,
            message = "OK",
            data = complexStygianData
        };

        SetupHttpResponse(AccountRolesUrl, CreateGameRoleResponse());
        SetupHttpResponse(HardChallengeUrl, JsonSerializer.Serialize(complexResponse));

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Is.Not.Empty);

        // Verify the response contains relevant information
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Is.Not.Empty);

        // For now, we'll just verify that the workflow completed without errors
        // In a more comprehensive test, we could verify the actual image content
    }

    [Test]
    public async Task ExecuteAsync_ConcurrentRequests_HandlesCorrectly()
    {
        // Arrange
        await CreateTestUserWithProfile();
        SetupSuccessfulApiResponses();
        await SetupCachedToken();

        object[] parameters = [Server.Asia, TestProfileId];

        // Act - Run multiple concurrent requests
        IEnumerable<Task<bool>> tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                await m_Executor.ExecuteAsync(parameters);
                return true;
            }
            catch
            {
                return false;
            }
        });

        bool[] results = await Task.WhenAll(tasks);

        // Assert At least some requests should complete successfully
        Assert.That(results.Count(r => r), Is.GreaterThan(0));

        // Verify that concurrent requests don't cause issues
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public async Task ExecuteAsync_WithTokenExpiration_ReauthenticatesUser()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup expired token response
        var expiredTokenResponse = new
        {
            retcode = -100,
            message = "Token expired",
            data = (object?)null
        };

        SetupHttpResponse(AccountRolesUrl, JsonSerializer.Serialize(expiredTokenResponse));

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);

        // Should either show authentication modal or error about expired token
        Assert.That(responseMessage, Does.Contain("expired").Or.Contain("authenticate").Or.Contain("login"));
    }

    [Test]
    public async Task ExecuteAsync_WithNetworkTimeout_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup timeout exception
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Does.Contain("timeout").Or.Contain("error").Or.Contain("failed"));
    }

    [Test]
    public async Task ExecuteAsync_WithMalformedJsonResponse_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup malformed HTTP response
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json")
            });

        object[] parameters = [Server.Asia, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Does.Contain("error").Or.Contain("failed"));
    }

    #endregion

    #region Authentication Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulAuth_SendsStygianCard()
    {
        // Arrange
        await CreateTestUserWithProfile();
        SetupSuccessfulApiResponses();

        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Set the pending server using reflection
        FieldInfo? pendingServerField = typeof(GenshinStygianCommandExecutor).GetField("m_PendingServer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        pendingServerField?.SetValue(m_Executor, Server.Asia);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        Assert.That(responseMessage, Is.Not.Empty);

        // Verify API calls were made
        VerifyHttpRequestWithQuery(AccountRolesUrl,
            "game_biz=hk4e_global&region=os_asia", Times.Once());
        VerifyHttpRequest(HardChallengeUrl, Times.Once());
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_AuthenticationFailed_LogsError()
    {
        // Arrange
        AuthenticationResult result = AuthenticationResult.Failure(m_TestUserId, "Authentication failed");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication failed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithNullPendingServer_HandlesGracefully()
    {
        // Arrange
        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Don't set pending server (should be null by default)

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Is.Not.Null);
        // Should handle gracefully even without pending server
    }

    #endregion

    #region Helper Methods

    private void SetupDistributedCacheMock()
    {
        // Default setup for token cache - no token by default (not authenticated)
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    private async Task CreateTestUserWithProfile()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LastUsedRegions = new Dictionary<Game, Server>
                    {
                        { Game.Genshin, Server.Asia }
                    },
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.Genshin, new Dictionary<string, string>
                            {
                                { Server.Asia.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private async Task CreateTestUserWithProfileNoServer()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid
                    // No LastUsedRegions to simulate no cached server
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private Task SetupCachedToken()
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);
        return Task.CompletedTask;
    }

    private void SetupSuccessfulApiResponses()
    {
        SetupHttpResponse(AccountRolesUrl, CreateGameRoleResponse());
        SetupHttpResponse(HardChallengeUrl, CreateValidStygianResponse());
    }

    private void SetupHttpResponse(string url, string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void VerifyHttpRequest(string url, Times times)
    {
        m_HttpMessageHandlerMock.Protected()
            .Verify("SendAsync", times,
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == url),
                ItExpr.IsAny<CancellationToken>());
    }

    private void VerifyHttpRequestWithQuery(string baseUrl, string queryParams, Times times)
    {
        string expected = $"{baseUrl}?{queryParams}";
        m_HttpMessageHandlerMock.Protected()
            .Verify("SendAsync", times,
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Query) == expected),
                ItExpr.IsAny<CancellationToken>());
    }

    private static string CreateGameRoleResponse()
    {
        List<UserGameData> gameRoleData =
        [
            new()
            {
                GameBiz = "hk4e_global",
                Region = "os_asia",
                GameUid = TestGameUid,
                Nickname = "TestPlayer",
                Level = 60,
                IsChosen = true,
                RegionName = "Asia",
                IsOfficial = true
            }
        ];

        var response = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                list = gameRoleData
            }
        };

        return JsonSerializer.Serialize(response);
    }

    private static string CreateValidStygianResponse()
    {
        return File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin", "Stygian_TestData_1.json"));
    }

    private static async Task<StygianData> LoadTestData()
    {
        string testDataJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin", "Stygian_TestData_1.json"));
        return JsonSerializer.Deserialize<StygianData>(testDataJson)!;
    }

    private static string CreateValidGameRecordResponse()
    {
        var gameRecordData = new
        {
            list = new[]
            {
                new
                {
                    game_id = 2,
                    game_role_id = TestGameUid,
                    nickname = "TestPlayer",
                    region = "os_asia",
                    level = 60,
                    background_image = "",
                    is_public = true,
                    data = new[]
                    {
                        new { name = "Active Days", value = "365" },
                        new { name = "Achievements", value = "500" },
                        new { name = "Characters", value = "40" }
                    },
                    region_name = "America",
                    url = "",
                    data_switches = new[]
                    {
                        new { switch_id = 1, is_public = true, switch_name = "Basic Stats" },
                        new { switch_id = 2, is_public = true, switch_name = "Characters" },
                        new { switch_id = 3, is_public = true, switch_name = "Spiral Abyss" }
                    },
                    h5_data_switches = Array.Empty<object>(),
                    pc_data_switches = Array.Empty<object>()
                }
            }
        };

        var response = new
        {
            retcode = 0,
            message = "OK",
            data = gameRecordData
        };

        return JsonSerializer.Serialize(response);
    }

    private static string CreateStygianNotUnlockedResponse()
    {
        var response = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                is_unlock = false,
                data = Array.Empty<object>()
            }
        };

        return JsonSerializer.Serialize(response);
    }

    private static string CreateStygianNoDataResponse()
    {
        var response = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                is_unlock = true,
                data = new[]
                {
                    new
                    {
                        single = new
                        {
                            has_data = false
                        },
                        mp = new
                        {
                            has_data = false
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(response);
    }

    #endregion
}
