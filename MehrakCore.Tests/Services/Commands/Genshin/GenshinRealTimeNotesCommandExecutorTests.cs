#region

using Mehrak.Application.Services.Genshin.RealTimeNotes;
using Mehrak.Bot.Executors.Genshin;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi.Genshin.Types;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Genshin.RealTimeNotes;
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

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinRealTimeNotesCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";
    private const string TestGameUid = "800000000";

    private GenshinRealTimeNotesCommandExecutor m_Executor = null!;
    private Mock<IRealTimeNotesApiService<GenshinRealTimeNotesData>> m_ApiServiceMock = null!;
    private ImageRepository m_ImageRepository = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<GenshinRealTimeNotesCommandExecutor>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private RedisCacheService m_TokenCacheService = null!;

    [SetUp]
    public void Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();

        // Create mocks for dependencies
        m_ApiServiceMock = new Mock<IRealTimeNotesApiService<GenshinRealTimeNotesData>>();
        m_LoggerMock = new Mock<ILogger<GenshinRealTimeNotesCommandExecutor>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Set up authentication middleware to return TestGuid
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Set up mocked HTTP handler and client factory
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Create real services with mocked dependencies
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        m_TokenCacheService = new RedisCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<RedisCacheService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up default distributed cache behavior
        SetupDistributedCacheMock();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Set up interaction context
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Set up Discord test helper to capture responses
        m_DiscordTestHelper.SetupRequestCapture(); // Create the service under test
        m_Executor = new GenshinRealTimeNotesCommandExecutor(
            m_ApiServiceMock.Object,
            m_ImageRepository,
            m_GameRecordApiService,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_LoggerMock.Object
        )
        {
            Context = m_ContextMock.Object
        };
    }

    [TearDown]
    public async Task TearDown()
    {
        m_DiscordTestHelper.Dispose();
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
    }

    private void SetupDistributedCacheMock()
    {
        // Default setup for token cache - no token by default (not authenticated)
        SetupTokenCacheForNotAuthenticated();
    }

    private void SetupTokenCacheForAuthenticated()
    {
        // Setup token cache to return a valid token (user is authenticated)
        // GetStringAsync is an extension method, so we need to mock the underlying GetAsync method
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);
    }

    private void SetupTokenCacheForNotAuthenticated()
    {
        // Setup token cache to return null (user is not authenticated)
        // GetStringAsync is an extension method, so we need to mock the underlying GetAsync method
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    #region Test Methods

    [Test]
    public void ExecuteAsync_WhenParametersAreInvalid_ThrowsArgumentException()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 0;
        int invalid = 1;
        ArgumentException? ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await m_Executor.ExecuteAsync(server, profile, invalid));

        Assert.That(ex.Message, Contains.Substring("Invalid parameters count").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoProfiles_SendsErrorResponse()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 1;

        UserModel user = new()
        {
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = []
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoMatchingProfile_SendsErrorResponse()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 2; // Non-existent profile

        UserModel user = CreateTestUser();
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerSpecifiedAndNoCachedServer_SendsErrorResponse()
    {
        // Arrange
        Regions? server = null;
        uint profile = 1;

        UserModel user = CreateTestUser();
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found. Please select a server"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserNotAuthenticated_SendsAuthenticationModal()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 1;

        UserModel user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            m_TestUserId, It.IsAny<IAuthenticationListener>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenApiReturnsError_SendsErrorResponse()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 1;

        UserModel user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return error
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(Result<GenshinRealTimeNotesData>.Failure(HttpStatusCode.TooManyRequests,
                "API Error: Rate limit exceeded"));

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("rate limit").IgnoreCase | Contains.Substring("error").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_WhenUserAuthenticated_DefersResponseAndFetchesNotes()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 1;

        UserModel user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return success
        GenshinRealTimeNotesData notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(Result<GenshinRealTimeNotesData>.Success(notesData));

        // Setup game record API
        SetupGameRecordApi();

        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), It.IsAny<string>(), TestLtUid, TestLToken), Times.Once);

        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WhenImageGenerationSucceeds_SendsImageResponse()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 1;

        UserModel user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return success
        GenshinRealTimeNotesData notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(Result<GenshinRealTimeNotesData>.Success(notesData));

        // Setup game record API
        SetupGameRecordApi();

        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes, Is.Not.Empty);
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
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulResult_FetchesNotesAndSendsCard()
    {
        // Arrange
        Regions server = Regions.Asia;
        uint profile = 1;

        UserModel user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Execute first to set up pending state
        await m_Executor.ExecuteAsync(server, profile);

        AuthenticationResult result = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Setup API service to return success
        GenshinRealTimeNotesData notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(Result<GenshinRealTimeNotesData>.Success(notesData));

        // Setup game record API
        SetupGameRecordApi();

        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Empty);

        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), It.IsAny<string>(), TestLtUid, TestLToken), Times.Once);
    }

    #endregion

    #region Helper Methods

    private UserModel CreateTestUser()
    {
        return new UserModel
        {
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LToken = TestLToken
                }
            ]
        };
    }

    private UserModel CreateTestUserWithCachedServer(Regions server)
    {
        return new UserModel
        {
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.Genshin,
                            new Dictionary<string, string> { { server.ToString(), TestGameUid } }
                        }
                    },
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.Genshin, server }
                    }
                }
            ]
        };
    }

    private void SetupAuthenticatedUser()
    {
        // Set up the token cache to return a valid token (using GetStringAsync as TokenCacheService does)
        SetupTokenCacheForAuthenticated();
    }

    private void SetupGameRecordApi()
    {
        var gameRecordResponse = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                list = new[]
                {
                    new
                    {
                        game_uid = TestGameUid,
                        nickname = "TestPlayer",
                        level = 60
                    }
                }
            }
        };

        StringContent responseContent = new(
            JsonSerializer.Serialize(gameRecordResponse),
            Encoding.UTF8,
            "application/json");

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null && req.RequestUri.ToString().Contains("getUserGameRolesByLtoken",
                        StringComparison.OrdinalIgnoreCase)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = responseContent
            });
    }

    private static GenshinRealTimeNotesData CreateTestNotesData()
    {
        return new GenshinRealTimeNotesData
        {
            CurrentResin = 120,
            MaxResin = 160,
            ResinRecoveryTime = "10000",
            FinishedTaskNum = 3,
            TotalTaskNum = 4,
            IsExtraTaskRewardReceived = false,
            RemainResinDiscountNum = 2,
            ResinDiscountNumLimit = 3,
            CurrentExpeditionNum = 4,
            MaxExpeditionNum = 5,
            Expeditions =
            [
                new() { AvatarSideIcon = "icon1", Status = "Ongoing", RemainedTime = "10000" },
                new() { AvatarSideIcon = "icon2", Status = "Finished", RemainedTime = "10000" }
            ],
            CurrentHomeCoin = 1200,
            MaxHomeCoin = 2400,
            HomeCoinRecoveryTime = "10000",
            CalendarUrl = "",
            Transformer = new Transformer
            {
                Obtained = true,
                RecoveryTime = new RecoveryTime
                {
                    Day = 2,
                    Hour = 5,
                    Minute = 30,
                    Second = 45,
                    Reached = false
                }
            }
        };
    }

    #endregion
}
