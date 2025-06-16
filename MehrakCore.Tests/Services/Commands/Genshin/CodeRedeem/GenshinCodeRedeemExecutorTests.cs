#region

using System.Net;
using System.Text;
using System.Text.Json;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Genshin.CodeRedeem;
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

namespace MehrakCore.Tests.Services.Commands.Genshin.CodeRedeem;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCodeRedeemExecutorTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestCode = "TESTCODE123";
    private const string TestGameUid = "123456789";

    private GenshinCodeRedeemExecutor m_Executor = null!;
    private Mock<ICodeRedeemApiService<GenshinCommandModule>> m_CodeRedeemApiServiceMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<GenshinCommandModule>> m_LoggerMock = null!;
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
        // Initialize mocks
        m_CodeRedeemApiServiceMock = new Mock<ICodeRedeemApiService<GenshinCommandModule>>();
        m_LoggerMock = new Mock<ILogger<GenshinCommandModule>>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MongoTestHelper = new MongoTestHelper();

        // Setup HTTP client
        var httpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient); // Initialize services
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);

        // Setup token cache to return cached tokens
        SetupTokenCache();

        // Initialize executor
        m_Executor = new GenshinCodeRedeemExecutor(
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_CodeRedeemApiServiceMock.Object,
            m_LoggerMock.Object);

        // Setup Discord interaction
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "code",
            ("code", TestCode, ApplicationCommandOptionType.String),
            ("server", "America", ApplicationCommandOptionType.String),
            ("profile", 1, ApplicationCommandOptionType.Integer));

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        m_Executor.Context = m_ContextMock.Object;
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper?.Dispose();
        m_MongoTestHelper?.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_ValidCodeAndUser_ShouldRedeemCodeSuccessfully()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);

        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Code redeemed successfully"));
    }

    [Test]
    public void ExecuteAsync_InvalidParameters_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await m_Executor.ExecuteAsync("invalid", "parameters"));
    }

    [Test]
    public void ExecuteAsync_EmptyCode_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await m_Executor.ExecuteAsync("", Regions.America, 1u));
    }

    [Test]
    public async Task ExecuteAsync_UserNotFound_ShouldSendErrorResponse()
    {
        // Arrange - Don't create a user

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_ProfileNotFound_ShouldSendErrorResponse()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        // Don't add profile to user

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 2u); // Non-existent profile

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_CodeRedemptionFails_ShouldSendErrorResponse()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiFailure();

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Code redemption failed"));
    }

    [Test]
    public async Task ExecuteAsync_CodeToUpperCase_ShouldConvertCodeToUpperCase()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        var lowercaseCode = "testcode123";

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(lowercaseCode, Regions.America, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            lowercaseCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_SuccessfulAuth_ShouldRedeemCode()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Set pending parameters by calling ExecuteAsync first
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.AtLeastOnce);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_FailedAuth_ShouldSendErrorResponse()
    {
        // Arrange
        var authResult = AuthenticationResult.Failure(TestUserId, "Authentication failed");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Authentication failed"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_MissingPendingParameters_ShouldSendErrorResponse()
    {
        // Arrange
        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act - Don't set pending parameters first
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Missing required parameters"));
    }

    [Test]
    public async Task ExecuteAsync_GameRecordApiFails_ShouldSendErrorResponse()
    {
        // Arrange
        var user = await CreateTestUserWithoutGameUid();

        SetupHttpResponseForGameRecordFailure();

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No game information found. Please select the correct region"));
    }

    [Test]
    public async Task ExecuteAsync_WithNullServer_ShouldUseCachedServer()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(TestCode, null, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DefaultProfile_ShouldUseProfile1()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, null);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ExceptionDuringExecution_ShouldSendErrorResponse()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Setup to throw an exception
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test exception"));

        SetupHttpResponseForGameRecord(CreateTestGameRecord());

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u); // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while redeeming the code. Please try again later."));
    }

    private async Task<UserModel> CreateTestUserAsync()
    {
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.America },
                        { GameName.HonkaiStarRail, Regions.America }
                    },
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { Regions.America.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            }
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
        return user;
    }

    private async Task<UserModel> CreateTestUserWithoutAuth()
    {
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.America },
                        { GameName.HonkaiStarRail, Regions.America }
                    }
                }
            }
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
        return user;
    }

    private async Task<UserModel> CreateTestUserWithoutGameUid()
    {
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.America },
                        { GameName.HonkaiStarRail, Regions.America }
                    }
                    // No GameUids - this will trigger the API call
                }
            }
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
        return user;
    }

    private static object CreateTestGameRecord()
    {
        return new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                list = new[]
                {
                    new
                    {
                        game_id = 2,
                        game_role_id = TestGameUid,
                        nickname = "TestPlayer",
                        region = "os_usa",
                        level = 60,
                        region_name = "America"
                    }
                }
            }
        };
    }

    private void SetupHttpResponseForGameRecord(object gameRecord)
    {
        var json = JsonSerializer.Serialize(gameRecord);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);
    }

    private void SetupHttpResponseForGameRecordFailure()
    {
        var errorResponse = new
        {
            retcode = -1,
            message = "Failed to get game record"
        };

        var json = JsonSerializer.Serialize(errorResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);
    }

    private void SetupCodeRedeemApiSuccess()
    {
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Success("Code redeemed successfully"));
    }

    private void SetupCodeRedeemApiFailure()
    {
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Failure(HttpStatusCode.BadRequest, "Code redemption failed"));
    }

    private void SetupTokenCache()
    {
        // Setup distributed cache to return cached token for authenticated users
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));
    }
}
