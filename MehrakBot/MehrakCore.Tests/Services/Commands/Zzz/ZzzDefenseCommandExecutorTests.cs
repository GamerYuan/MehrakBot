using Mehrak.Application.Services.Zzz.Defense;
using Mehrak.Bot.Executors.Zzz;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Services.Commands.Zzz;
using MehrakCore.Services.Commands.Zzz.Defense;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NetCord.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MehrakCore.Tests.Services.Commands.Zzz;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzDefenseCommandExecutorTests
{
    private ZzzDefenseCommandExecutor m_Executor;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private UserRepository m_UserRepository;
    private ZzzDefenseCardService m_CardService;
    private ZzzDefenseApiService m_ApiService;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock;
    private Mock<IDistributedCache> m_DistributedCacheMock;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareServiceMock;
    private Mock<IInteractionContext> m_ContextMock;
    private Mock<ZzzImageUpdaterService> m_ImageUpdaterMock;
    private DiscordTestHelper m_DiscordTestHelper;
    private HttpClient m_HttpClient;

    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestGameUid = "1300000000";
    private const string TestLToken = "test_ltoken_value";
    private const uint TestProfileId = 1;

    private string m_TestDataStr;
    private byte[] m_GoldenImage;

    private static readonly string AccountRolesUrl =
        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken";
    private static readonly string DefenseApiUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record_zzz/api/zzz/challenge";

    [SetUp]
    public async Task Setup()
    {
        m_DiscordTestHelper = new DiscordTestHelper();
        m_DiscordTestHelper.SetupRequestCapture();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        m_HttpMessageHandlerMock = new();
        m_HttpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock = new();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(m_HttpClient);

        m_DistributedCacheMock = new();
        RedisCacheService tokenCacheService = new(m_DistributedCacheMock.Object, Mock.Of<ILogger<RedisCacheService>>());
        m_AuthenticationMiddlewareServiceMock = new();
        GameRecordApiService gameRecordApiService = new(m_HttpClientFactoryMock.Object, Mock.Of<ILogger<GameRecordApiService>>());

        m_ContextMock = new();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId));

        ImageRepository imageRepository = new(MongoTestHelper.Instance.MongoDbService, Mock.Of<ILogger<ImageRepository>>());
        m_UserRepository = new(MongoTestHelper.Instance.MongoDbService, Mock.Of<ILogger<UserRepository>>());
        m_CardService = new ZzzDefenseCardService(imageRepository, Mock.Of<ILogger<ZzzDefenseCardService>>());
        m_ApiService = new ZzzDefenseApiService(m_HttpClientFactoryMock.Object, Mock.Of<ILogger<ZzzDefenseApiService>>());
        m_ImageUpdaterMock = new(imageRepository, m_HttpClientFactoryMock.Object, Mock.Of<ILogger<ZzzImageUpdaterService>>());
        m_Executor = new ZzzDefenseCommandExecutor(m_ApiService, m_CardService,
            m_ImageUpdaterMock.Object, m_UserRepository,
            tokenCacheService, m_AuthenticationMiddlewareServiceMock.Object,
            gameRecordApiService, Mock.Of<ILogger<ZzzDefenseCommandExecutor>>())
        {
            Context = m_ContextMock.Object
        };

        SetupImageUpdaterNoop();
        m_TestDataStr = LoadTestData();
        m_GoldenImage = await
            File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets", "Shiyu_GoldenImage_1.jpg"));
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
        m_DiscordTestHelper.Dispose();
    }

    #region Tests

    [Test]
    public async Task ExecuteAsync_WithCorrectData_ShouldSendImageStream()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiSuccess();
        SetupApiServiceWithData();
        await m_CardService.InitializeAsync();

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes, Is.EqualTo(m_GoldenImage));
    }

    [Test]
    public void ExecuteAsync_WithInvalidParameters_ShouldThrowException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync());
    }

    [Test]
    public async Task ExecuteAsync_WithoutUserInDatabase_ShouldSendErrorMessage()
    {
        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithUnauthorizedGameRecordApi_ShouldSendInvalidCookiesMessage()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiUnauthorized();

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task ExecuteAsync_NoTokenCache_ShouldTriggerAuthModal()
    {
        await CreateUserAsync();
        SetupNullTokenCache();
        m_AuthenticationMiddlewareServiceMock.Setup(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor))
            .Returns(Guid.NewGuid().ToString());

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("Authenticate"));
        m_AuthenticationMiddlewareServiceMock.Verify(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NullServerAndNoCachedServer_ShouldError()
    {
        // Create user without LastUsedRegions to force missing cached server
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles = [ new UserProfile
            {
                ProfileId = TestProfileId,
                LtUid = TestLtUid,
                GameUids = []
            }]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);
        SetupBasicTokenCache();
        await m_Executor.ExecuteAsync(null, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("No cached server found"));
    }

    [Test]
    public async Task ExecuteAsync_WithDefenseApiNoData_ShouldSendNoRecordsMessage()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiSuccess();
        SetupApiServiceNoData();

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("No Shiyu Defense clear records found"));
    }

    [Test]
    public async Task ExecuteAsync_WithDefenseApiHasDataButEmptyFloorList_ShouldSendNoRecordsMessage()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiSuccess();
        // custom defense response with empty floor list
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == DefenseApiUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"retcode\":0,\"message\":\"OK\",\"data\":{\"has_data\":true,\"begin_time\":\"0\",\"end_time\":\"0\",\"rating_list\":[],\"all_floor_detail\":[]}}", Encoding.UTF8, "application/json")
            });

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("No Shiyu Defense clear records found"));
    }

    [Test]
    public async Task ExecuteAsync_DefenseApiInvalidCookies_ShouldSendInvalidCookiesMessage()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiSuccess();
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == DefenseApiUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"retcode\":10001,\"message\":\"Invalid\",\"data\":null}", Encoding.UTF8, "application/json")
            });

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task ExecuteAsync_DefenseApiErrorRetcode_ShouldSendGenericDefenseErrorMessage()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiSuccess();
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == DefenseApiUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"retcode\":-1,\"message\":\"Err\",\"data\":null}", Encoding.UTF8, "application/json")
            });

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("An error occurred while fetching Shiyu Defense data"));
    }

    [Test]
    public async Task ExecuteAsync_DefenseApiNonSuccessStatus_ShouldSendUnknownErrorMessage()
    {
        await CreateUserAsync();
        SetupBasicTokenCache();
        SetupGameRoleApiSuccess();
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == DefenseApiUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId);
        string? message = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(message, Does.Contain("An unknown error occurred when accessing HoYoLAB API"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_Success_ShouldSendImage()
    {
        await CreateUserAsync();
        // No token initially -> triggers auth modal and sets pending server
        SetupNullTokenCache();
        m_AuthenticationMiddlewareServiceMock.Setup(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor))
            .Returns(Guid.NewGuid().ToString());
        SetupGameRoleApiSuccess();
        SetupApiServiceWithData();
        await m_CardService.InitializeAsync();

        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId); // sets pending server

        // simulate authentication completion
        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes, Is.EqualTo(m_GoldenImage));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_Failure_ShouldNotSendDefenseCard()
    {
        await CreateUserAsync();
        SetupNullTokenCache();
        m_AuthenticationMiddlewareServiceMock.Setup(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor))
            .Returns(Guid.NewGuid().ToString());
        await m_Executor.ExecuteAsync(Server.Asia, TestProfileId); // sets pending server

        AuthenticationResult failure = AuthenticationResult.Failure(m_TestUserId, "fail");
        await m_Executor.OnAuthenticationCompletedAsync(failure);
        // Should only have modal/auth related output, no golden image bytes
        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Null);
    }

    #endregion Tests

    #region Helpers

    private static UserModel CreateTestUser(ulong userId)
    {
        return new UserModel
        {
            Id = userId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.ZenlessZoneZero, new Dictionary<string, string>
                            {
                                { nameof(Server.Asia), TestGameUid }
                            }
                        }
                    },
                    LastUsedRegions = new Dictionary<Game, Server>
                    {
                        { Game.ZenlessZoneZero, Server.Asia }
                    }
                }
            ]
        };
    }

    private void SetupGameRoleApi(HttpStatusCode statusCode, string content)
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

    private static string LoadTestData()
    {
        string testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz", "Shiyu_TestData_1.json");
        return File.ReadAllText(testDataPath);
    }

    private async Task CreateUserAsync()
    {
        UserModel user = CreateTestUser(m_TestUserId);
        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private void SetupBasicTokenCache()
    {
        m_DistributedCacheMock.Setup(x =>
            x.GetAsync(It.Is<string>(key => key.Equals($"TokenCache_{m_TestUserId}_{TestLtUid}")), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));
    }

    private void SetupNullTokenCache()
    {
        m_DistributedCacheMock.Setup(x =>
            x.GetAsync(It.Is<string>(key => key.Equals($"TokenCache_{m_TestUserId}_{TestLtUid}")), It.IsAny<CancellationToken>()))
        .ReturnsAsync((byte[]?)null);
    }

    private void SetupGameRoleApiSuccess()
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
                        region = "prod_gf_jp",
                        game_biz = "nap_global",
                        nickname = "Test",
                        level = 60,
                        is_chosen = false,
                        region_name = "Asia",
                        is_official = true
                    }
                }
            }
        };

        string gameDataJson = JsonSerializer.Serialize(gameDataResponse);
        SetupGameRoleApi(HttpStatusCode.OK, gameDataJson);
    }

    private void SetupGameRoleApiUnauthorized()
    {
        var errorResponse = new { retcode = -100, message = "Login expired. Please log in again." };
        string json = JsonSerializer.Serialize(errorResponse);
        SetupGameRoleApi(HttpStatusCode.OK, json);
    }

    private void SetupImageUpdaterNoop()
    {
        m_ImageUpdaterMock
            .Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);
        m_ImageUpdaterMock
            .Setup(x => x.UpdateSideAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);
        m_ImageUpdaterMock
            .Setup(x => x.UpdateBuddyImageAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupApiServiceWithData()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == DefenseApiUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"retcode\":0,\"message\":\"OK\",\"data\":" + m_TestDataStr + "}", Encoding.UTF8, "application/json")
            });
    }

    private void SetupApiServiceNoData()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null &&
                                                     req.RequestUri!.GetLeftPart(UriPartial.Path) == DefenseApiUrl),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"retcode\":0,\"message\":\"OK\",\"data\":{\"has_data\":false,\"begin_time\":\"1756411200\",\"end_time\":\"1757620799\",\"all_floor_detail\":[],\"rating_list\":[]}}", Encoding.UTF8, "application/json")
            });
    }

    #endregion Helpers
}
