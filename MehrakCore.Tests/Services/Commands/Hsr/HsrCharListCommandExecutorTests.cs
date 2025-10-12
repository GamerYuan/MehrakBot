using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Bot.Executors.Hsr;
using Mehrak.Domain.Interfaces;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Services;
using MehrakCore.Services.Commands.Hsr;
using MehrakCore.Services.Commands.Hsr.CharList;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NetCord.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharListCommandExecutorTests
{
    private HsrCharListCommandExecutor m_CommandExecutor;
    private HsrCharListCardService m_CommandService;
    private Mock<HsrImageUpdaterService> m_ImageUpdaterServiceMock;
    private Mock<ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>> m_CharacterApiMock;
    private UserRepository m_UserRepository;
    private Mock<RedisCacheService> m_TokenCacheServiceMock;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock;
    private GameRecordApiService m_GameRecordApiService;
    private Mock<ILogger<HsrCharListCommandExecutor>> m_LoggerMock;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock;
    private Mock<IDistributedCache> m_DistributedCacheMock;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private HttpClient m_HttpClient;
    private DiscordTestHelper m_DiscordTestHelper;

    private ulong m_TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestGameUid = "800000000";
    private const string TestLToken = "test_ltoken_value";
    private const uint TestProfileId = 1;

    private Mock<IInteractionContext> m_ContextMock;

    private static readonly string AccountRolesUrl =
        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken";

    [SetUp]
    public void Setup()
    {
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(m_HttpClient);

        ImageRepository imageRepository =
            new(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);
        // Initialize mocks
        m_CommandService = new HsrCharListCardService(
            imageRepository,
            Mock.Of<ILogger<HsrCharListCardService>>());

        m_ImageUpdaterServiceMock = new Mock<HsrImageUpdaterService>(
            imageRepository,
            m_HttpClientFactoryMock.Object,
            Mock.Of<IRelicRepository>(),
            NullLogger<HsrImageUpdaterService>.Instance);

        m_DistributedCacheMock = new Mock<IDistributedCache>();

        m_CharacterApiMock = new Mock<ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>>();
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheServiceMock =
            new Mock<RedisCacheService>(m_DistributedCacheMock.Object, NullLogger<RedisCacheService>.Instance);
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);
        m_LoggerMock = new Mock<ILogger<HsrCharListCommandExecutor>>();

        m_DiscordTestHelper = new DiscordTestHelper();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId));

        // Create the command executor
        m_CommandExecutor = new HsrCharListCommandExecutor(
            m_CommandService,
            m_ImageUpdaterServiceMock.Object,
            m_CharacterApiMock.Object,
            m_UserRepository,
            m_TokenCacheServiceMock.Object,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object
        )
        {
            Context = m_ContextMock.Object
        };
    }

    [TearDown]
    public void TearDown()
    {
        m_HttpClient.Dispose();
        m_DiscordTestHelper.Dispose();
    }

    #region Helpers

    private static UserModel CreateTestUser(ulong userId, ulong ltUid = TestLtUid, uint profileId = TestProfileId)
    {
        return new UserModel
        {
            Id = userId,
            Profiles =
            [
                new()
                {
                    ProfileId = profileId,
                    LtUid = ltUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { nameof(Regions.Asia), TestGameUid }
                            }
                        }
                    },
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.HonkaiStarRail, Regions.Asia }
                    }
                }
            ]
        };
    }

    private void SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode statusCode, string content)
    {
        m_HttpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == AccountRolesUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }

    private static HsrBasicCharacterData LoadHsrTestCharacterData()
    {
        string testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", "CharList_TestData_1.json");
        using FileStream fs = File.OpenRead(testDataPath);
        return JsonSerializer.Deserialize<HsrBasicCharacterData>(fs, new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        })!;
    }

    private async Task CreateUserAsync(UserModel user)
    {
        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private void SetupTokenCache(string token)
    {
        m_DistributedCacheMock.Setup(x =>
            x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Encoding.UTF8.GetBytes(token));
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
                        region_name = "Asia",
                        is_official = true
                    }
                }
            }
        };

        string gameDataJson = JsonSerializer.Serialize(gameDataResponse);
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, gameDataJson);
    }

    private void SetupGameRecordApiUnauthorized()
    {
        var errorResponse = new { retcode = -100, message = "Invalid cookies" };
        string json = JsonSerializer.Serialize(errorResponse);
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, json);
    }

    private void SetupGameRecordApiError(int retcode = 100, string message = "Error")
    {
        var errorResponse = new { retcode, message };
        string json = JsonSerializer.Serialize(errorResponse);
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, json);
    }

    private void SetupCharacterApiWithData()
    {
        HsrBasicCharacterData basicData = LoadHsrTestCharacterData();
        IEnumerable<HsrBasicCharacterData> list = [basicData];
        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(list);
    }

    private void SetupCharacterApiNoData()
    {
        // Return an empty or null AvatarList effectively
        IEnumerable<HsrBasicCharacterData> list = [new HsrBasicCharacterData()];
        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(list);
    }

    private void SetupImageUpdaterNoop()
    {
        m_ImageUpdaterServiceMock
            .Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);
        m_ImageUpdaterServiceMock
            .Setup(x => x.UpdateSideAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);
    }

    #endregion Helpers

    #region ExecuteAsync Tests

    [Test]
    public async Task ExecuteAsync_ValidParameters_GeneratesCardAndUpdatesImages()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);
        SetupTokenCache(TestLToken);
        SetupGameRecordApiSuccess();
        SetupCharacterApiWithData();
        SetupImageUpdaterNoop();

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeastOnce);

        byte[]? response = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(response, Is.Not.Null);
        Assert.That(response, Is.Not.Empty);
    }

    [Test]
    public async Task ExecuteAsync_NoCharactersFound_SendsErrorMessage()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);
        SetupTokenCache(TestLToken);
        SetupGameRecordApiSuccess();
        SetupCharacterApiNoData();
        SetupImageUpdaterNoop();

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No characters found"));
    }

    [Test]
    public async Task ExecuteAsync_NullServerWithNoCachedServer_SendsError()
    {
        // Arrange: user with no LastUsedRegions
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    GameUids = [],
                    LastUsedRegions = []
                }
            ]
        };
        await CreateUserAsync(user);
        SetupTokenCache(TestLToken);

        // Act
        await m_CommandExecutor.ExecuteAsync(null, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No cached server").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_NoToken_RequestsAuthentication()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);
        m_DistributedCacheMock.Setup(x =>
            x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
        .ReturnsAsync((byte[]?)null);

        string guid = Guid.NewGuid().ToString();
        m_AuthenticationMiddlewareMock
            .Setup(x => x.RegisterAuthenticationListener(m_TestUserId, m_CommandExecutor))
            .Returns(guid)
            .Verifiable();

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_AuthenticationMiddlewareMock.Verify();
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("custom_id").Or.Contain("modal"));
    }

    [Test]
    public async Task ExecuteAsync_GameRecordUnauthorized_SendsReauthMessage()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);
        SetupTokenCache(TestLToken);
        SetupGameRecordApiUnauthorized();
        SetupCharacterApiWithData();

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("authenticate"));
    }

    [Test]
    public async Task ExecuteAsync_GameRecordFailure_SendsGenericError()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);
        SetupTokenCache(TestLToken);
        SetupGameRecordApiError();

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Failed to retrieve game profile").IgnoreCase);
    }

    #endregion ExecuteAsync Tests

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_Failure_DoesNotProceed()
    {
        // Arrange
        AuthenticationResult result = AuthenticationResult.Failure(m_TestUserId, "Authentication failed");

        // Act
        await m_CommandExecutor.OnAuthenticationCompletedAsync(result);

        // Assert - no exception and no follow-up sent
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_Success_CompletesFlow()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);

        // Initiate auth flow to set pending server
        m_DistributedCacheMock.Setup(x =>
            x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
        .ReturnsAsync((byte[]?)null);
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Now complete auth
        SetupGameRecordApiSuccess();
        SetupCharacterApiWithData();
        SetupImageUpdaterNoop();

        AuthenticationResult auth = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_CommandExecutor.OnAuthenticationCompletedAsync(auth);

        // Assert
        byte[]? response = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();

        Assert.That(response, Is.Not.Null);
        Assert.That(response, Is.Not.Empty);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_Success_NoCharacters_SendsError()
    {
        // Arrange
        UserModel user = CreateTestUser(m_TestUserId);
        await CreateUserAsync(user);

        // Initiate auth flow to set pending server
        m_DistributedCacheMock.Setup(x =>
           x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
       .ReturnsAsync((byte[]?)null);
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Complete auth with no characters
        SetupGameRecordApiSuccess();
        SetupCharacterApiNoData();

        AuthenticationResult auth = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_CommandExecutor.OnAuthenticationCompletedAsync(auth);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No characters found"));
    }

    #endregion OnAuthenticationCompletedAsync Tests
}
