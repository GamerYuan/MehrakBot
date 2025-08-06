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

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCodeRedeemExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestCode = "TESTCODE123";
    private const string TestGameUid = "123456789";

    private GenshinCodeRedeemExecutor m_Executor = null!;
    private Mock<ICodeRedeemApiService<GenshinCommandModule>> m_CodeRedeemApiServiceMock = null!;
    private Mock<ICodeRedeemRepository> m_CodeRedeemRepositoryMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<GenshinCommandModule>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
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
        m_CodeRedeemRepositoryMock = new Mock<ICodeRedeemRepository>();
        m_LoggerMock = new Mock<ILogger<GenshinCommandModule>>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();

        // Setup HTTP client
        var httpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient); // Initialize services
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
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
            m_CodeRedeemRepositoryMock.Object,
            m_CodeRedeemApiServiceMock.Object,
            m_LoggerMock.Object);

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Setup Discord interaction
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId, "code",
            ("code", TestCode, ApplicationCommandOptionType.String),
            ("server", "America", ApplicationCommandOptionType.String),
            ("profile", 1, ApplicationCommandOptionType.Integer));

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        m_Executor.Context = m_ContextMock.Object;
    }

    [TearDown]
    public async Task TearDown()
    {
        m_DiscordTestHelper.Dispose();
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
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
    public async Task OnAuthenticationCompletedAsync_SuccessfulAuth_ShouldRedeemCode()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Set pending parameters by calling ExecuteAsync first
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        var authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

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
        var authResult = AuthenticationResult.Failure(m_TestUserId, "Authentication failed");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Authentication failed"));
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
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u); // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An unknown error occurred while processing your request"));
    }

    [Test]
    public async Task ExecuteAsync_NoCodeProvided_ShouldRedeemCachedCodes()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        var cachedCodes = new List<string> { "CACHED1", "CACHED2" };

        SetupHttpResponseForGameRecord(gameRecord);

        // Reset any previous mock setups that might interfere
        m_CodeRedeemApiServiceMock.Reset();

        // Setup the API to handle BOTH cached codes specifically
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                "CACHED1",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Success("Code redeemed successfully"))
            .Verifiable();

        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                "CACHED2",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Success("Code redeemed successfully"))
            .Verifiable();

        // Setup repository to return cached codes
        m_CodeRedeemRepositoryMock.Setup(x => x.GetCodesAsync(GameName.Genshin))
            .ReturnsAsync(cachedCodes)
            .Verifiable();

        // Act
        await m_Executor.ExecuteAsync("", Regions.America, 1u);

        // Assert
        // Verify all setup calls were made
        m_CodeRedeemApiServiceMock.Verify();
        m_CodeRedeemRepositoryMock.Verify();

        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Code redeemed successfully"));

        // Verify that codes were added to the repository
        m_CodeRedeemRepositoryMock.Verify(x => x.AddCodesAsync(GameName.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(list => list.Count == cachedCodes.Count &&
                                                              list.All(kvp =>
                                                                  cachedCodes.Contains(kvp.Key.ToUpperInvariant())))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MultipleCodesProvided_ShouldRedeemAllCodes()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        var multipleCodes = "CODE1, CODE2,CODE3";
        var expectedCodes = new[] { "CODE1", "CODE2", "CODE3" };

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(multipleCodes, Regions.America, 1u);

        // Assert
        foreach (var code in expectedCodes)
            m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
                code.ToUpperInvariant(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                TestLtUid,
                TestLToken), Times.Once);

        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Code redeemed successfully"));

        // Verify that codes were added to the repository
        m_CodeRedeemRepositoryMock.Verify(x => x.AddCodesAsync(GameName.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(list => list.Count == expectedCodes.Length &&
                                                              expectedCodes.All(c =>
                                                                  list.ContainsKey(c.ToUpperInvariant())))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulCodeRedemption_ShouldAddToRepository()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();

        SetupHttpResponseForGameRecord(gameRecord);
        SetupCodeRedeemApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(TestCode, Regions.America, 1u);

        // Assert
        // Verify the code was redeemed
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            TestCode.ToUpperInvariant(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);

        // Verify the code was added to the repository
        m_CodeRedeemRepositoryMock.Verify(x => x.AddCodesAsync(GameName.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(list => list.ContainsKey(TestCode.ToUpperInvariant()))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CodeRedemptionFailure_ShouldStopAndSendErrorMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        var codes = "VALID1, INVALID1, VALID2";

        SetupHttpResponseForGameRecord(gameRecord);

        // Setup first code to succeed and second to fail
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                "VALID1",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Success("Code redeemed successfully"));

        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(
                "INVALID1",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Failure(HttpStatusCode.BadRequest, "Code redemption failed"));

        // Act
        await m_Executor.ExecuteAsync(codes, Regions.America, 1u);

        // Assert
        // Verify only the first two codes were attempted
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            "VALID1",
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);

        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            "INVALID1",
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Once);

        // Third code should not be attempted since second failed
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            "VALID2",
            It.IsAny<string>(),
            It.IsAny<string>(),
            TestLtUid,
            TestLToken), Times.Never);

        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Response should contain the error message
        Assert.That(response, Does.Contain("Code redemption failed"));

        // No codes should be added to the repository since the process failed
        m_CodeRedeemRepositoryMock.Verify(x => x.AddCodesAsync(GameName.Genshin,
            It.IsAny<Dictionary<string, CodeStatus>>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_EmptyInputAndNoCachedCodes_ShouldSendErrorMessage()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        SetupHttpResponseForGameRecord(gameRecord);

        // Explicitly configure empty result for GetCodesAsync
        m_CodeRedeemRepositoryMock.Setup(x => x.GetCodesAsync(GameName.Genshin))
            .ReturnsAsync([]);

        // Act
        await m_Executor.ExecuteAsync("", Regions.America, 1u);

        // Assert
        // Should attempt to get codes from repository
        m_CodeRedeemRepositoryMock.Verify(x =>
            x.GetCodesAsync(It.Is<GameName>(input =>
                input.Equals(GameName.Genshin))), Times.Once); // Verify all verifiable expectations

        // No codes should be redeemed
        m_CodeRedeemApiServiceMock.Verify(x => x.RedeemCodeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ulong>(),
            It.IsAny<string>()), Times.Never);

        // Should send error response
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No known codes found in database"));
    }

    [Test]
    public async Task ExecuteAsync_ExpiredCode_ShouldRemoveFromCache()
    {
        // Arrange
        await CreateTestUserAsync();
        var gameRecord = CreateTestGameRecord();
        SetupHttpResponseForGameRecord(gameRecord);

        CodeRedeemRepository codeRedeemRepo =
            new(MongoTestHelper.Instance.MongoDbService, NullLogger<CodeRedeemRepository>.Instance);
        GenshinCodeRedeemExecutor executor = new(
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            codeRedeemRepo,
            m_CodeRedeemApiServiceMock.Object,
            m_LoggerMock.Object);

        List<string> codes = ["EXPIREDCODE", "VALIDCODE"];
        await codeRedeemRepo.AddCodesAsync(GameName.Genshin,
            codes.ToDictionary(code => code, _ => CodeStatus.Valid));
        executor.Context = m_ContextMock.Object;

        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync(It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Success("Code redeemed successfully"));
        m_CodeRedeemApiServiceMock.Setup(x => x.RedeemCodeAsync("EXPIREDCODE",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ulong>(),
                It.IsAny<string>()))
            .ReturnsAsync(ApiResult<string>.Success("Expired code", -2001));

        // Act
        await executor.ExecuteAsync("", Regions.America, 1u);

        // Assert
        m_CodeRedeemApiServiceMock.Verify(x =>
            x.RedeemCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ulong>(),
                It.IsAny<string>()), Times.Exactly(2));
        var codesInDb = await codeRedeemRepo.GetCodesAsync(GameName.Genshin);

        Assert.That(codesInDb, Does.Not.Contain("EXPIREDCODE"));
        Assert.That(codesInDb, Does.Contain("VALIDCODE"));
    }

    private async Task CreateTestUserAsync()
    {
        var user = new UserModel
        {
            Id = m_TestUserId,
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
            Id = m_TestUserId,
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
}
