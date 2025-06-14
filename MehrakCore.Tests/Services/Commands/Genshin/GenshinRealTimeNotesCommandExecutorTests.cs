#region

using System.Net;
using System.Text;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
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

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinRealTimeNotesCommandExecutorTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";
    private const string TestGameUid = "test_game_uid_12345";

    private GenshinRealTimeNotesCommandExecutor m_Executor = null!;
    private Mock<IRealTimeNotesApiService<GenshinRealTimeNotesData>> m_ApiServiceMock = null!;
    private ImageRepository m_ImageRepository = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<GenshinRealTimeNotesData>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private MongoTestHelper m_MongoTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private TokenCacheService m_TokenCacheService = null!;

    [SetUp]
    public void Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MongoTestHelper = new MongoTestHelper();

        // Create mocks for dependencies
        m_ApiServiceMock = new Mock<IRealTimeNotesApiService<GenshinRealTimeNotesData>>();
        m_LoggerMock = new Mock<ILogger<GenshinRealTimeNotesData>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Set up authentication middleware to return TestGuid
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Set up mocked HTTP handler and client factory
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Create real services with mocked dependencies
        m_ImageRepository = new ImageRepository(m_MongoTestHelper.MongoDbService, NullLogger<ImageRepository>.Instance);
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up default distributed cache behavior
        SetupDistributedCacheMock();

        // Set up interaction context
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);
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

        // Setup test image assets
        SetupTestImageAssets();
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
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
        var tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
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

    private void SetupTestImageAssets()
    {
        // Use real images from the Assets folder
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin");
        var imageNames = new[]
            { "genshin_resin", "genshin_expedition", "genshin_teapot", "genshin_weekly", "genshin_transformer" };

        foreach (var imageName in imageNames)
        {
            var imagePath = Path.Combine(assetsPath, $"{imageName}.png");
            if (!File.Exists(imagePath)) continue;

            var imageBytes = File.ReadAllBytes(imagePath);
            m_ImageRepository.UploadFileAsync(imageName, new MemoryStream(imageBytes)).GetAwaiter().GetResult();
        }
    }

    #region Test Methods

    [Test]
    public void ExecuteAsync_WhenParametersAreInvalid_ThrowsArgumentException()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 0;
        int invalid = 1;
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await m_Executor.ExecuteAsync(server, profile, invalid));

        Assert.That(ex.Message, Contains.Substring("Invalid parameters count").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoProfiles_SendsErrorResponse()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = new UserModel
        {
            Id = TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>()
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoMatchingProfile_SendsErrorResponse()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 2; // Non-existent profile

        var user = CreateTestUser();
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerSpecifiedAndNoCachedServer_SendsErrorResponse()
    {
        // Arrange
        Regions? server = null;
        uint profile = 1;

        var user = CreateTestUser();
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found. Please select a server"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserNotAuthenticated_SendsAuthenticationModal()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            TestUserId, It.IsAny<IAuthenticationListener>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenApiReturnsError_SendsErrorResponse()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return error
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<GenshinRealTimeNotesData>.Failure(HttpStatusCode.TooManyRequests,
                "API Error: Rate limit exceeded"));

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("rate limit").IgnoreCase | Contains.Substring("error").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_WhenUserAuthenticated_DefersResponseAndFetchesNotes()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return success
        var notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<GenshinRealTimeNotesData>.Success(notesData));

        // Setup game record API
        SetupGameRecordApi();

        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), It.IsAny<string>(), TestLtUid, TestLToken), Times.Once);

        var bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WhenImageGenerationSucceeds_SendsImageResponse()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return success
        var notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<GenshinRealTimeNotesData>.Success(notesData));

        // Setup game record API
        SetupGameRecordApi();

        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulResult_FetchesNotesAndSendsCard()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Execute first to set up pending state
        await m_Executor.ExecuteAsync(server, profile);

        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Setup API service to return success
        var notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<GenshinRealTimeNotesData>.Success(notesData));

        // Setup game record API
        SetupGameRecordApi();

        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        var bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Empty);

        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), It.IsAny<string>(), TestLtUid, TestLToken), Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithFailedResult_SendsErrorMessage()
    {
        // Arrange
        var result = AuthenticationResult.Failure(TestUserId, "Invalid authentication token");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("authentication") | Contains.Substring("error"));
    }

    #endregion

    #region Helper Methods

    private UserModel CreateTestUser()
    {
        return new UserModel
        {
            Id = TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LToken = TestLToken
                }
            }
        };
    }

    private UserModel CreateTestUserWithCachedServer(Regions server)
    {
        return new UserModel
        {
            Id = TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin,
                            new Dictionary<string, string> { { server.ToString(), TestGameUid } }
                        }
                    },
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, server }
                    }
                }
            }
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
            data = TestGameUid
        };

        var responseContent = new StringContent(
            JsonSerializer.Serialize(gameRecordResponse),
            Encoding.UTF8,
            "application/json");

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null && req.RequestUri.ToString().Contains("game_record")),
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
            Expeditions = new List<Expedition>
            {
                new() { AvatarSideIcon = "icon1", Status = "Ongoing", RemainedTime = "10000" },
                new() { AvatarSideIcon = "icon2", Status = "Finished", RemainedTime = "10000" }
            },
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