#region

using System.Net;
using System.Text;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Hsr.RealTimeNotes;
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

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrRealTimeNotesCommandExecutorTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";
    private const string TestGameUid = "test_game_uid_12345";

    private HsrRealTimeNotesCommandExecutor m_Executor = null!;
    private Mock<IRealTimeNotesApiService<HsrRealTimeNotesData>> m_ApiServiceMock = null!;
    private ImageRepository m_ImageRepository = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<HsrRealTimeNotesCommandExecutor>> m_LoggerMock = null!;
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
        m_ApiServiceMock = new Mock<IRealTimeNotesApiService<HsrRealTimeNotesData>>();
        m_LoggerMock = new Mock<ILogger<HsrRealTimeNotesCommandExecutor>>();
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
        m_DiscordTestHelper.SetupRequestCapture();

        // Create the service under test
        m_Executor = new HsrRealTimeNotesCommandExecutor(
            m_ApiServiceMock.Object,
            m_UserRepository,
            m_ImageRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object
        )
        {
            Context = m_ContextMock.Object
        };

        // Setup test image assets
        SetupTestImageAssets();
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
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        var imageNames = new[] { "hsr_tbp", "hsr_assignment", "hsr_weekly", "hsr_rogue" };

        foreach (var imageName in imageNames)
        {
            var imagePath = Path.Combine(assetsPath, $"{imageName}.png");
            if (File.Exists(imagePath))
            {
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
                m_ImageRepository.UploadFileAsync(imageName, fileStream, "image/png").GetAwaiter().GetResult();
            }
            else
            {
                // Fallback: create a minimal test image if the asset doesn't exist
                var testImageData = new byte[]
                {
                    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
                    0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
                    0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
                    0x42, 0x60, 0x82
                };
                using var memoryStream = new MemoryStream(testImageData);
                m_ImageRepository.UploadFileAsync(imageName, memoryStream, "image/png").GetAwaiter().GetResult();
            }
        }
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_WhenParametersCountInvalid_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync("param1").AsTask());
        Assert.That(ex.Message, Contains.Substring("Invalid parameters count"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserDoesNotExist_SendsErrorResponse()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

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
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(notesData));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), It.IsAny<string>(), TestLtUid, TestLToken), Times.Once);
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
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Failure(HttpStatusCode.BadRequest, "API Error"));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An error occurred: API Error"));
    }

    [Test]
    public async Task ExecuteAsync_WhenSuccessful_BuildsAndSendsRealTimeNotesCard()
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
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(notesData));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("HSR Real-Time Notes"));
        Assert.That(response, Contains.Substring("Trailblaze Power"));
        Assert.That(response, Contains.Substring("Assignments"));
        Assert.That(response, Contains.Substring("Echoes of War"));
        Assert.That(response, Contains.Substring("Simulated Universe"));
    }

    [Test]
    public async Task ExecuteAsync_WhenExceptionThrown_SendsGenericErrorResponse()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to throw exception
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An unknown error occurred, please try again later"));
    }

    [Test]
    public async Task ExecuteAsync_WithCachedServerFromProfile_UsesCorrectServer()
    {
        // Arrange
        Regions? server = null; // No server specified
        uint profile = 1;
        var cachedServer = Regions.Europe;

        var user = CreateTestUserWithCachedServer(cachedServer);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return success
        var notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(notesData));

        // Setup game record API for the cached server region
        SetupGameRecordApiForSuccessfulResponse(cachedServer);

        // Act
        await m_Executor.ExecuteAsync(server, profile); // Assert
        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), "prod_official_eur", TestLtUid, TestLToken), Times.Once);
    }

    #endregion

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenAuthenticationSuccessful_SendsRealTimeNotes()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup API service to return success
        var notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(notesData));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(
            server); // Set up the executor with a pending server (normally set during ExecuteAsync)
        await m_Executor.ExecuteAsync(server, profile); // This will trigger authentication

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        m_ApiServiceMock.Verify(x => x.GetRealTimeNotesAsync(
            It.IsAny<string>(), It.IsAny<string>(), TestLtUid, TestLToken), Times.AtLeastOnce);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenAuthenticationFailed_SendsErrorResponse()
    {
        // Arrange
        var authResult = AuthenticationResult.Failure(TestUserId, "Authentication failed");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Authentication failed: Authentication failed"));
    }

    #endregion

    #region BuildRealTimeNotes Tests

    [Test]
    public async Task BuildRealTimeNotes_CreatesCorrectMessageStructure()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser();

        // Setup API service to return success with specific data
        var notesData = CreateTestNotesData();
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(notesData));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Check for main sections
        Assert.That(response, Contains.Substring($"HSR Real-Time Notes (UID: {TestGameUid})"));
        Assert.That(response, Contains.Substring("Trailblaze Power"));
        Assert.That(response, Contains.Substring($"{notesData.CurrentStamina}/{notesData.MaxStamina}"));
        Assert.That(response, Contains.Substring("Assignments"));
        Assert.That(response, Contains.Substring("Echoes of War"));
        Assert.That(response, Contains.Substring("Simulated Universe"));
        Assert.That(response, Contains.Substring($"{notesData.CurrentRogueScore}/{notesData.MaxRogueScore}"));
    }

    [Test]
    public async Task BuildRealTimeNotes_WhenStaminaFull_ShowsCorrectMessage()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser(); // Setup API service to return success with full stamina
        var notesData = CreateTestNotesData();
        // Create a version with full stamina
        var fullStaminaData = new HsrRealTimeNotesData
        {
            CurrentStamina = notesData.MaxStamina,
            MaxStamina = notesData.MaxStamina,
            StaminaRecoverTime = notesData.StaminaRecoverTime,
            StaminaFullTs = notesData.StaminaFullTs,
            AcceptedExpeditionNum = notesData.AcceptedExpeditionNum,
            TotalExpeditionNum = notesData.TotalExpeditionNum,
            Expeditions = notesData.Expeditions,
            CurrentTrainScore = notesData.CurrentTrainScore,
            MaxTrainScore = notesData.MaxTrainScore,
            CurrentRogueScore = notesData.CurrentRogueScore,
            MaxRogueScore = notesData.MaxRogueScore,
            WeeklyCocoonCnt = notesData.WeeklyCocoonCnt,
            WeeklyCocoonLimit = notesData.WeeklyCocoonLimit,
            CurrentReserveStamina = notesData.CurrentReserveStamina,
            IsReserveStaminaFull = notesData.IsReserveStaminaFull,
            RogueTournWeeklyUnlocked = notesData.RogueTournWeeklyUnlocked,
            RogueTournWeeklyMax = notesData.RogueTournWeeklyMax,
            RogueTournWeeklyCur = notesData.RogueTournWeeklyCur,
            CurrentTs = notesData.CurrentTs,
            RogueTournExpIsFull = notesData.RogueTournExpIsFull
        };
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(fullStaminaData));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Already Full!"));
    }

    [Test]
    public async Task BuildRealTimeNotes_WhenNoExpeditions_ShowsCorrectMessage()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser(); // Setup API service to return success with no expeditions
        var baseNotesData = CreateTestNotesData();
        var noExpeditionsData = new HsrRealTimeNotesData
        {
            CurrentStamina = baseNotesData.CurrentStamina,
            MaxStamina = baseNotesData.MaxStamina,
            StaminaRecoverTime = baseNotesData.StaminaRecoverTime,
            StaminaFullTs = baseNotesData.StaminaFullTs,
            AcceptedExpeditionNum = 0,
            TotalExpeditionNum = baseNotesData.TotalExpeditionNum,
            Expeditions = baseNotesData.Expeditions,
            CurrentTrainScore = baseNotesData.CurrentTrainScore,
            MaxTrainScore = baseNotesData.MaxTrainScore,
            CurrentRogueScore = baseNotesData.CurrentRogueScore,
            MaxRogueScore = baseNotesData.MaxRogueScore,
            WeeklyCocoonCnt = baseNotesData.WeeklyCocoonCnt,
            WeeklyCocoonLimit = baseNotesData.WeeklyCocoonLimit,
            CurrentReserveStamina = baseNotesData.CurrentReserveStamina,
            IsReserveStaminaFull = baseNotesData.IsReserveStaminaFull,
            RogueTournWeeklyUnlocked = baseNotesData.RogueTournWeeklyUnlocked,
            RogueTournWeeklyMax = baseNotesData.RogueTournWeeklyMax,
            RogueTournWeeklyCur = baseNotesData.RogueTournWeeklyCur,
            CurrentTs = baseNotesData.CurrentTs,
            RogueTournExpIsFull = baseNotesData.RogueTournExpIsFull
        };
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(noExpeditionsData));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("None Accepted!"));
        Assert.That(response, Contains.Substring("To be dispatched"));
    }

    [Test]
    public async Task BuildRealTimeNotes_WhenWeeklyFullyClaimed_ShowsCorrectMessage()
    {
        // Arrange
        var server = Regions.America;
        uint profile = 1;

        var user = CreateTestUserWithCachedServer(server);
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Set up authenticated user
        SetupAuthenticatedUser(); // Setup API service to return success with fully claimed weekly
        var baseNotesData = CreateTestNotesData();
        var fullyClaimed = new HsrRealTimeNotesData
        {
            CurrentStamina = baseNotesData.CurrentStamina,
            MaxStamina = baseNotesData.MaxStamina,
            StaminaRecoverTime = baseNotesData.StaminaRecoverTime,
            StaminaFullTs = baseNotesData.StaminaFullTs,
            AcceptedExpeditionNum = baseNotesData.AcceptedExpeditionNum,
            TotalExpeditionNum = baseNotesData.TotalExpeditionNum,
            Expeditions = baseNotesData.Expeditions,
            CurrentTrainScore = baseNotesData.CurrentTrainScore,
            MaxTrainScore = baseNotesData.MaxTrainScore,
            CurrentRogueScore = baseNotesData.CurrentRogueScore,
            MaxRogueScore = baseNotesData.MaxRogueScore,
            WeeklyCocoonCnt = 0, // 0 means fully claimed
            WeeklyCocoonLimit = baseNotesData.WeeklyCocoonLimit,
            CurrentReserveStamina = baseNotesData.CurrentReserveStamina,
            IsReserveStaminaFull = baseNotesData.IsReserveStaminaFull,
            RogueTournWeeklyUnlocked = baseNotesData.RogueTournWeeklyUnlocked,
            RogueTournWeeklyMax = baseNotesData.RogueTournWeeklyMax,
            RogueTournWeeklyCur = baseNotesData.RogueTournWeeklyCur,
            CurrentTs = baseNotesData.CurrentTs,
            RogueTournExpIsFull = baseNotesData.RogueTournExpIsFull
        };
        m_ApiServiceMock.Setup(x => x.GetRealTimeNotesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()))
            .ReturnsAsync(ApiResult<HsrRealTimeNotesData>.Success(fullyClaimed));

        // Setup game record API for the correct region
        SetupGameRecordApiForSuccessfulResponse(server);

        // Act
        await m_Executor.ExecuteAsync(server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Fully Claimed!"));
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
                            GameName.HonkaiStarRail,
                            new Dictionary<string, string> { { server.ToString(), TestGameUid } }
                        }
                    },
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.HonkaiStarRail, server }
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

    private string CreateValidGameRecordResponse(Regions region = Regions.Asia)
    {
        var regionMapping = region switch
        {
            Regions.Asia => "prod_official_asia",
            Regions.Europe => "prod_official_eur",
            Regions.America => "prod_official_usa",
            Regions.Sar => "prod_official_cht",
            _ => "prod_official_asia"
        };

        var gameRecord = new
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
                        region = regionMapping,
                        level = 70,
                        region_name = region.ToString()
                    }
                }
            }
        };

        return JsonSerializer.Serialize(gameRecord);
    }

    private string CreateInvalidAuthGameRecordResponse()
    {
        var gameRecord = new
        {
            retcode = -100,
            data = (object?)null
        };

        return JsonSerializer.Serialize(gameRecord);
    }

    private void SetupGameRecordApiForSuccessfulResponse(Regions region = Regions.Asia)
    {
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(region));
    }

    private void SetupGameRecordApiForFailure()
    {
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateInvalidAuthGameRecordResponse());
    }

    private void SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode statusCode, string content)
    {
        var response = new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void SetupGameRecordApi()
    {
        // For backward compatibility - default to successful response for Asia region
        SetupGameRecordApiForSuccessfulResponse(Regions.Asia);
    }

    private HsrRealTimeNotesData CreateTestNotesData()
    {
        return new HsrRealTimeNotesData
        {
            CurrentStamina = 150,
            MaxStamina = 240,
            StaminaRecoverTime = 3600,
            StaminaFullTs = (int)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            AcceptedExpeditionNum = 2,
            TotalExpeditionNum = 4,
            Expeditions = new List<Expedition>
            {
                new()
                {
                    Name = "Test Expedition 1",
                    Status = "Ongoing",
                    FinishTs = (int)DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(),
                    RemainingTime = 7200
                },
                new()
                {
                    Name = "Test Expedition 2",
                    Status = "Ongoing",
                    FinishTs = (int)DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds(),
                    RemainingTime = 14400
                }
            },
            CurrentTrainScore = 500,
            MaxTrainScore = 500,
            CurrentRogueScore = 8000,
            MaxRogueScore = 14000,
            WeeklyCocoonCnt = 2,
            WeeklyCocoonLimit = 3,
            CurrentReserveStamina = 0,
            IsReserveStaminaFull = false,
            RogueTournWeeklyUnlocked = true,
            RogueTournWeeklyMax = 4000,
            RogueTournWeeklyCur = 2000,
            CurrentTs = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            RogueTournExpIsFull = false
        };
    }

    #endregion
}
