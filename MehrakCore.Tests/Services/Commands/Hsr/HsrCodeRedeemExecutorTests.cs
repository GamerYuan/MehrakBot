#region

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Hsr.CodeRedeem;
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
public class HsrCodeRedeemExecutorTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestCode = "TESTCODE123";
    private const string TestGameUid = "123456789";

    private HsrCodeRedeemExecutor m_Executor = null!;
    private Mock<ICodeRedeemApiService<HsrCommandModule>> m_CodeRedeemApiServiceMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<HsrCommandModule>> m_LoggerMock = null!;
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
        m_CodeRedeemApiServiceMock = new Mock<ICodeRedeemApiService<HsrCommandModule>>();
        m_LoggerMock = new Mock<ILogger<HsrCommandModule>>();
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
            .Returns(httpClient);

        // Initialize services
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);

        // Setup token cache to return cached tokens
        SetupTokenCache();

        // Initialize executor
        m_Executor = new HsrCodeRedeemExecutor(
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
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_ValidCodeAndUser_ShouldRedeemCodeSuccessfully()
    {
        // Arrange
        await CreateTestUserAsync();
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
    public void ExecuteAsync_NullCode_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await m_Executor.ExecuteAsync(null, Regions.America, 1u));
    }

    [Test]
    public void ExecuteAsync_WhitespaceCode_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await m_Executor.ExecuteAsync("   ", Regions.America, 1u));
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
        await CreateTestUserAsync();
        // Don't add profile to user

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 2u); // Non-existent profile

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_NoServerSelectedAndNoCachedServer_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserWithoutCachedServer();

        // Act
        await m_Executor.ExecuteAsync(TestCode, null, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No cached server found. Please select a server"));
    }

    [Test]
    public async Task ExecuteAsync_CodeRedemptionFails_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();
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
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        const string lowercaseCode = "testcode123";

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
    public async Task ExecuteAsync_CodeWithWhitespace_ShouldTrimCode()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        const string codeWithWhitespace = "  TESTCODE123  ";

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(codeWithWhitespace, Regions.America, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            codeWithWhitespace.Trim().ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UserRequiresAuthentication_ShouldTriggerAuthFlow()
    {
        // Arrange
        await CreateTestUserAsync();
        SetupTokenCacheEmpty(); // No cached token

        var guidString = Guid.NewGuid().ToString();
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<HsrCodeRedeemExecutor>()))
            .Returns(guidString);

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(TestUserId, m_Executor),
            Times.Once);

        // Verify that modal was sent (this would be captured by the DiscordTestHelper)
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        // The response should contain the authentication modal, but exact content depends on implementation
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_SuccessfulAuth_ShouldRedeemCode()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Set pending parameters by calling ExecuteAsync first (with no cached token)
        SetupTokenCacheEmpty();
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
            TestLToken), Times.Once);
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
    public async Task OnAuthenticationCompletedAsync_MissingPendingCode_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set only server but not code (simulate partial state)
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Manually clear the pending code to simulate missing parameter
        var executorType = typeof(HsrCodeRedeemExecutor);
        var pendingCodeField = executorType.GetField("m_PendingCode", BindingFlags.NonPublic | BindingFlags.Instance);
        pendingCodeField?.SetValue(m_Executor, string.Empty);

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Missing required parameters"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_MissingPendingServer_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set only code but not server (simulate partial state)
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Manually clear the pending server to simulate missing parameter
        var executorType = typeof(HsrCodeRedeemExecutor);
        var pendingServerField =
            executorType.GetField("m_PendingServer", BindingFlags.NonPublic | BindingFlags.Instance);
        pendingServerField?.SetValue(m_Executor, null);

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Missing required parameters"));
    }

    [Test]
    public async Task ExecuteAsync_GameRecordApiFails_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserWithoutGameUid();

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
        await CreateTestUserAsync();
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
        await CreateTestUserAsync();
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
        await CreateTestUserAsync();

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
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while redeeming the code. Please try again later."));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_ExceptionDuringExecution_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Setup to throw an exception
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test exception"));

        SetupHttpResponseForGameRecord(CreateTestGameRecord());

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while redeeming the code. Please try again later."));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_NullContext_ShouldHandleGracefully()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, null!);

        // Act & Assert - Should not throw exception
        Assert.DoesNotThrowAsync(async () => await m_Executor.OnAuthenticationCompletedAsync(authResult));
    }

    [Test]
    [TestCase(Regions.America, "prod_official_usa")]
    [TestCase(Regions.Europe, "prod_official_eur")]
    [TestCase(Regions.Asia, "prod_official_asia")]
    [TestCase(Regions.Sar, "prod_official_cht")]
    public async Task ExecuteAsync_AllAvailableRegions_ShouldUseCorrectRegions(Regions region, string regionString)
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecordForRegion(GetGameRecordRegion(region));

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(TestCode, region, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode,
            regionString,
            TestGameUid,
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithUpdatedContext_ShouldUseNewContext()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Set pending parameters by calling ExecuteAsync first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        var newContextMock = new Mock<IInteractionContext>();
        var newInteraction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "code");
        newContextMock.Setup(x => x.Interaction).Returns(newInteraction);

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, newContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert - The executor should use the new context
        Assert.That(m_Executor.Context, Is.EqualTo(newContextMock.Object));
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task RedeemCodeAsync_GameUidRetrievalFails_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserWithoutGameUid();

        // Setup HTTP response to return error (simulating API failure)
        var errorResponse = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                list = Array.Empty<object>()
            }
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

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No game information found. Please select the correct region"));
    }

    [Test]
    public async Task ExecuteAsync_HttpRequestThrowsException_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserWithoutGameUid();

        // Setup HTTP message handler to throw exception
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while redeeming the code. Please try again later."));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_GameUidUpdateFails_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserWithoutGameUid();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Setup HTTP response for game record failure
        SetupHttpResponseForGameRecordFailure();

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No game information found. Please select the correct region"));
    }

    [Test]
    public async Task ExecuteAsync_WithDifferentCodeFormats_ShouldNormalizeCode()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        const string mixedCaseCode = "TeSt123CoDe";

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(mixedCaseCode, Regions.America, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            mixedCaseCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ProfileNotInUser_ShouldSendErrorResponse()
    {
        // Arrange
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 2, // Different profile ID
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.HonkaiStarRail, Regions.America }
                    }
                }
            }
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act - Request profile ID 1 which doesn't exist
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_UserNotFoundAfterAuth_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Delete user from database to simulate user being deleted during auth process
        await m_UserRepository.DeleteUserAsync(TestUserId);

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No profile found. Please select the correct profile"));
    }

    [Test]
    public async Task ExecuteAsync_CodeRedeemServiceReturnsNonSuccessStatusCode_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);

        // Setup code redeem API to return a specific error code
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Failure(HttpStatusCode.Unauthorized, "Redemption Code Expired"));

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Redemption Code Expired"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_CodeRedeemServiceThrowsException_ShouldSendErrorResponse()
    {
        // Arrange
        await CreateTestUserAsync();

        // Set pending parameters first
        SetupTokenCacheEmpty();
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Setup game record API to work
        SetupHttpResponseForGameRecord(CreateTestGameRecord());

        // Setup code redeem API to throw exception during auth completion
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while redeeming the code. Please try again later."));
    }

    [Test]
    public async Task ExecuteAsync_ValidCodeWithSpecialCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        const string codeWithSpecialChars = "  test-code_123  ";

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(codeWithSpecialChars, Regions.America, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            codeWithSpecialChars.Trim().ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);
    }

    private async Task CreateTestUserAsync()
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
                            GameName.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { nameof(Regions.America), TestGameUid }
                            }
                        }
                    }
                }
            }
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private async Task CreateTestUserWithoutGameUid()
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
    }

    private async Task CreateTestUserWithoutCachedServer()
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
                    LToken = TestLToken
                    // No LastUsedRegions - this will cause server validation to fail
                }
            }
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private static object CreateTestGameRecord()
    {
        return CreateTestGameRecordForRegion("prod_official_usa");
    }

    private static object CreateTestGameRecordForRegion(string region)
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
                        game_uid = TestGameUid,
                        nickname = "TestPlayer",
                        level = 60,
                        region_name = GetRegionName(region)
                    }
                }
            }
        };
    }

    private static string GetGameRecordRegion(Regions region)
    {
        return region switch
        {
            Regions.America => "prod_official_usa",
            Regions.Europe => "prod_official_eur",
            Regions.Asia => "prod_official_asia",
            Regions.Sar => "prod_official_cht",
            _ => "prod_official_usa"
        };
    }

    private static string GetRegionName(string region)
    {
        return region switch
        {
            "os_usa" => "America",
            "os_euro" => "Europe",
            "os_asia" => "Asia",
            "os_cht" => "TW/HK/MO",
            _ => "America"
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
            retcode = 0,
            message = "OK",
            data = new
            {
                list = Array.Empty<object>()
            }
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

    private void SetupTokenCacheEmpty()
    {
        // Setup distributed cache to return null (no cached token)
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }
}
