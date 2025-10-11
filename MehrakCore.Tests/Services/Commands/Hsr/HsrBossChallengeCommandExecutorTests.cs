#region

using Mehrak.Application.Services.Hsr.EndGame.BossChallenge;
using Mehrak.Bot.Executors.Hsr.BossChallenge;
using Mehrak.Bot.Modules;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Hsr.Types;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Hsr.EndGame;
using MehrakCore.Services.Commands.Hsr.EndGame.BossChallenge;
using MehrakCore.Services.Commands.Hsr.EndGame.PureFiction;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrBossChallengeCommandExecutorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private ulong m_TestUserId;

    private UserRepository m_UserRepository = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthMiddlewareMock = null!;
    private HsrBossChallengeCommandExecutor m_Executor = null!;
    private Mock<HsrEndGameCardService> m_CommandServiceMock = null!;
    private Mock<HsrEndGameApiService> m_ApiServiceMock = null!;
    private Mock<ImageUpdaterService<HsrCharacterInformation>> m_ImageUpdaterServiceMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ILogger<HsrCommandModule>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private HttpClient m_HttpClient = null!;
    private ImageRepository m_ImageRepository = null!;

    private HsrEndInformation m_TestBossChallengeData = null!;
    private string m_BossChallengeTestDataJson = null!;

    private const uint TestProfileId = 1;
    private const string TestLToken = "test_ltoken_value";
    private const ulong TestLtUid = 123456789UL;
    private const string TestGameUid = "800000001";

    [SetUp]
    public void Setup()
    {
        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();
        m_DiscordTestHelper = new DiscordTestHelper();
        m_ContextMock = new Mock<IInteractionContext>();
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Setup repositories and services
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);

        m_AuthMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Setup HTTP mocking
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(m_HttpClient);

        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);

        // Setup service mocks
        m_CommandServiceMock =
            new Mock<HsrEndGameCardService>(m_ImageRepository, NullLogger<HsrEndGameCardService>.Instance);
        m_ApiServiceMock = new Mock<HsrEndGameApiService>(m_HttpClientFactoryMock.Object,
            NullLogger<HsrEndGameApiService>.Instance);
        m_ImageUpdaterServiceMock = new Mock<ImageUpdaterService<HsrCharacterInformation>>(
            m_ImageRepository, m_HttpClientFactoryMock.Object,
            NullLogger<ImageUpdaterService<HsrCharacterInformation>>.Instance);
        m_LoggerMock = new Mock<ILogger<HsrCommandModule>>();

        // Load test data
        LoadTestData();

        // Setup executor
        m_Executor = new HsrBossChallengeCommandExecutor(
            m_UserRepository,
            m_TokenCacheService,
            m_AuthMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object,
            m_CommandServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_ApiServiceMock.Object
        )
        {
            // Setup context
            Context = m_ContextMock.Object
        };

        SetupDistributedCacheMock();
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_HttpClient.Dispose();
    }

    #region Helper Methods

    private void LoadTestData()
    {
        // Load test data from file
        string testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", "As_TestData_1.json");
        m_BossChallengeTestDataJson = File.ReadAllText(testDataPath);
        m_TestBossChallengeData =
            JsonSerializer.Deserialize<HsrEndInformation>(m_BossChallengeTestDataJson, JsonOptions)!;
    }

    private void SetupDistributedCacheMock()
    {
        m_DistributedCacheMock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private async Task CreateTestUserAsync()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        { Game.HonkaiStarRail, new Dictionary<string, string> { { "America", TestGameUid } } }
                    },
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.HonkaiStarRail, Regions.Asia }
                    }
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private void SetupBossChallengeApiSuccess(HsrEndInformation? customData = null)
    {
        HsrEndInformation challengeData = customData ?? m_TestBossChallengeData;
        var challengeResponse = new
        {
            retcode = 0,
            data = challengeData
        };

        string challengeJson = JsonSerializer.Serialize(challengeResponse);
        HttpResponseMessage challengeHttpResponse = new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(challengeJson, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString()
                        .StartsWith($"{HoYoLabDomains.PublicApi}/event/game_record/hkrpg/api/challenge_boss")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(challengeHttpResponse);
    }

    private void SetupBossChallengeApiError(int retcode = 100, string message = "Error")
    {
        var errorResponse = new
        {
            retcode,
            message
        };

        string errorJson = JsonSerializer.Serialize(errorResponse);
        HttpResponseMessage errorHttpResponse = new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString()
                        .StartsWith($"{HoYoLabDomains.PublicApi}/event/game_record/hkrpg/api/challenge_boss")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(errorHttpResponse);
    }

    private void SetupBossChallengeNoData()
    {
        HsrEndInformation noDataChallenge = new()
        {
            Groups = [],
            StarNum = 0,
            MaxFloor = "0",
            BattleNum = 0,
            HasData = false,
            AllFloorDetail = [],
            MaxFloorId = 0
        };

        SetupBossChallengeApiSuccess(noDataChallenge);
    }

    private void SetupGameRecordApiSuccess()
    {
        var gameDataResponse = new
        {
            retcode = 0,
            data = new
            {
                list = new[]
                {
                    new
                    {
                        game_uid = TestGameUid,
                        region = "prod_official_asia",
                        game_biz = "hkrpg_global",
                        nickname = "TestPlayer",
                        level = 70,
                        is_chosen = false,
                        region_name = "America",
                        is_official = true
                    }
                }
            }
        };

        string gameDataJson = JsonSerializer.Serialize(gameDataResponse);
        HttpResponseMessage gameDataHttpResponse = new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(gameDataJson, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString()
                        .StartsWith($"{HoYoLabDomains.PublicApi}/event/game_record/card/wapi/getGameRecordCard")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(gameDataHttpResponse);
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString()
                        .StartsWith($"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(gameDataHttpResponse);
    }

    private void SetupTokenCacheEmpty()
    {
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    private void SetupTokenCacheWithData()
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);
    }

    #endregion

    #region ExecuteAsync Tests

    [Test]
    public async Task ExecuteAsync_ValidParameters_GeneratesCard()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeastOnce);

        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_ApiError_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiError();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("error"));
    }

    [Test]
    public async Task ExecuteAsync_UserNotFound_RequestsAuthentication()
    {
        // Arrange Don't create a user

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("authentication") | Does.Contain("profile"));
    }

    [Test]
    public async Task ExecuteAsync_ProfileNotFound_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserAsync();

        // Act - Use a different profile ID
        await m_Executor.ExecuteAsync(Regions.Asia, 999U);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("profile"));
    }

    [Test]
    public async Task ExecuteAsync_NoTokenInCache_RequestsAuthentication()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheEmpty();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_AuthMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoDataFound_SendsNoDataMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();
        SetupBossChallengeNoData();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No Apocalyptic Shadow clear records"));
    }

    [Test]
    public async Task ExecuteAsync_ExceptionThrown_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("error"));
    }

    [Test]
    public async Task ExecuteAsync_InvalidAuthenticationCredentials_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiError(10001, "Invalid credentials");

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Invalid") | Does.Contain("Cookies"));
    }

    [Test]
    public async Task ExecuteAsync_InvalidParameterCount_ThrowsArgumentException()
    {
        // Arrange
        await CreateTestUserAsync();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync(Regions.Asia));
    }

    [Test]
    public async Task ExecuteAsync_NullServerWithNoCachedServer_SendsErrorMessage()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        { Game.HonkaiStarRail, new Dictionary<string, string> { { "Asia", TestGameUid } } }
                    },
                    // No LastUsedRegions - this should trigger the error
                    LastUsedRegions = []
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
        SetupTokenCacheWithData();

        // Act
        await m_Executor.ExecuteAsync(null, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No cached server found"));
    }

    #endregion

    #region OnAuthenticationCompletedAsync Tests

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
    public async Task OnAuthenticationCompletedAsync_SuccessfulAuth_ContinuesExecution()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiSuccess();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_UserNotFoundAfterAuth_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Delete user from database to simulate user being deleted during auth process
        await m_UserRepository.DeleteUserAsync(m_TestUserId);

        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No profile found. Please select the correct profile"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_ExceptionDuringProcessing_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiSuccess();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("error"));
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public async Task ExecuteAsync_HttpException_SendsGenericErrorMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString()
                        .StartsWith($"{HoYoLabDomains.PublicApi}/event/game_record/hkrpg/api/challenge_boss")),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("error"));
    }

    [Test]
    public async Task ExecuteAsync_EmptyFloorDetails_SendsNoDataMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();

        HsrEndInformation emptyFloorData = new()
        {
            Groups =
            [
                new HsrEndGroup
                {
                    ScheduleId = 1,
                    BeginTime = new ScheduleTime { Year = 2024, Month = 1, Day = 1, Hour = 0, Minute = 0 },
                    EndTime = new ScheduleTime { Year = 2024, Month = 2, Day = 1, Hour = 0, Minute = 0 },
                    Status = "active",
                    Name = "Test Boss Challenge"
                }
            ],
            StarNum = 0,
            MaxFloor = "0",
            BattleNum = 0,
            HasData = true,
            AllFloorDetail = [], // Empty floor details
            MaxFloorId = 0
        };

        SetupBossChallengeApiSuccess(emptyFloorData);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No Apocalyptic Shadow clear records"));
    }

    [Test]
    public async Task ExecuteAsync_NullFloorDetailsNodes_SendsNoDataMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();

        HsrEndInformation nullNodeData = new()
        {
            Groups =
            [
                new HsrEndGroup
                {
                    ScheduleId = 1,
                    BeginTime = new ScheduleTime { Year = 2024, Month = 1, Day = 1, Hour = 0, Minute = 0 },
                    EndTime = new ScheduleTime { Year = 2024, Month = 2, Day = 1, Hour = 0, Minute = 0 },
                    Status = "active",
                    Name = "Test Boss Challenge"
                }
            ],
            StarNum = 0,
            MaxFloor = "1",
            BattleNum = 0,
            HasData = true,
            AllFloorDetail =
            [
                new HsrEndFloorDetail
                {
                    Name = "Floor 1",
                    RoundNum = 1,
                    StarNum = 0,
                    Node1 = null, // Null nodes
                    Node2 = null,
                    MazeId = 1,
                    IsFast = false
                }
            ],
            MaxFloorId = 1
        };

        SetupBossChallengeApiSuccess(nullNodeData);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No Apocalyptic Shadow clear records"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task Integration_AuthenticationFlow_WorksEndToEnd()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheEmpty(); // Force authentication
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiSuccess();

        // Act - Start execution (should trigger auth)
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Verify auth was requested
        m_AuthMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor), Times.Once);

        // Complete authentication
        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task Integration_MultipleRegions_HandlesCorrectly()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail,
                            new Dictionary<string, string>
                            {
                                { "America", TestGameUid },
                                { "Europe", "800000002" },
                                { "Asia", "800000003" }
                            }
                        }
                    },
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.HonkaiStarRail, Regions.Europe }
                    }
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
        SetupTokenCacheWithData();
        SetupGameRecordApiSuccess();
        SetupBossChallengeApiSuccess();

        // Act - Test different regions
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
    }

    #endregion
}
