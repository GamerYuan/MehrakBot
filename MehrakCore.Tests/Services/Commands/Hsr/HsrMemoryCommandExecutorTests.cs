#region

using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Bot.Executors.Hsr;
using Mehrak.Bot.Modules;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi;
using Mehrak.GameApi.Hsr.Types;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Hsr.Memory;
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

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrMemoryCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGameUid = "800000000";
    private const uint TestProfileId = 1;
    private const string TestGuid = "test-guid-12345";

    private HsrMemoryCommandExecutor m_Executor = null!;
    private HsrMemoryCardService m_CommandService = null!;
    private HsrMemoryApiService m_ApiService = null!;
    private Mock<ImageUpdaterService<HsrCharacterInformation>> m_ImageUpdaterServiceMock = null!;
    private UserRepository m_UserRepository = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
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

    private HsrMemoryInformation m_TestMemoryData = null!;
    private string m_MemoryTestDataJson = null!;

    [SetUp]
    public Task Setup()
    {
        // Generate random test user ID
        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Setup mocks
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_LoggerMock = new Mock<ILogger<HsrCommandModule>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);
        m_ImageUpdaterServiceMock = new Mock<ImageUpdaterService<HsrCharacterInformation>>(m_ImageRepository,
            m_HttpClientFactoryMock.Object, NullLogger<ImageUpdaterService<HsrCharacterInformation>>.Instance);

        // Setup HttpClient with mocked message handler
        m_HttpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(m_HttpClient);

        // Setup real services
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);

        // Create real service instances with mocked dependencies
        m_ApiService =
            new HsrMemoryApiService(m_HttpClientFactoryMock.Object, NullLogger<HsrMemoryApiService>.Instance);
        m_CommandService =
            new HsrMemoryCardService(m_ImageRepository, NullLogger<HsrMemoryCardService>.Instance);

        // Setup Discord context
        m_DiscordTestHelper = new DiscordTestHelper();
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Create executor with real service instances
        m_Executor = new HsrMemoryCommandExecutor(
            m_ApiService,
            m_ImageUpdaterServiceMock.Object,
            m_CommandService,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object)
        {
            Context = m_ContextMock.Object
        };

        // Setup authentication middleware
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        // Setup Discord test helper for request capture
        m_DiscordTestHelper.SetupRequestCapture();

        // Load test data
        LoadTestData();

        return Task.CompletedTask;
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
        m_DiscordTestHelper.Dispose();
        m_HttpClient.Dispose();
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_InvalidParameters_ThrowsException()
    {
        // Act & Assert - The implementation expects exactly 2 parameters
        ArgumentException? ex = Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync().AsTask());

        Assert.That(ex.Message, Does.Contain("Invalid number of parameters provided"));
    }

    [Test]
    public async Task ExecuteAsync_UserNotFound_ReturnsEarly()
    {
        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_ProfileNotFound_ReturnsEarly()
    {
        // Arrange
        await CreateTestUser();

        // Act - Use profile ID that doesn't exist
        await m_Executor.ExecuteAsync(Regions.Asia, 999u);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_NoServerProvided_NoCache_SendsError()
    {
        // Arrange
        await CreateTestUser();

        // Act
        await m_Executor.ExecuteAsync(null, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("No cached server found"));
    }

    [Test]
    public async Task ExecuteAsync_WithCachedServer_UsesCachedServer()
    {
        // Arrange
        await CreateTestUserWithCachedServer();
        SetupSuccessfulApiResponses();

        // Act
        await m_Executor.ExecuteAsync(null, TestProfileId);

        // Assert
        UserModel? user = await m_UserRepository.GetUserAsync(m_TestUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Profiles?.FirstOrDefault()?.LtUid, Is.EqualTo(TestLtUid));
    }

    [Test]
    public async Task ExecuteAsync_UserNotAuthenticated_StartsAuthenticationFlow()
    {
        // Arrange
        await CreateTestUser();
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            m_TestUserId, m_Executor), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UserAuthenticated_ExecutesMemoryCommand()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        UserModel? user = await m_UserRepository.GetUserAsync(m_TestUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Profiles?.FirstOrDefault()?.LtUid, Is.EqualTo(TestLtUid));

        // Verify HTTP calls were made
        m_HttpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_ApiError_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupGameDataApiSuccess();
        SetupMemoryApiFailure();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("An unknown error occurred"));

        m_LoggerMock.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to fetch Memory of Chaos information")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoMemoryData_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupGameDataApiSuccess();
        HsrMemoryInformation emptyMemoryData = new()
        {
            ScheduleId = 1,
            StartTime = new ScheduleTime { Year = 2025, Month = 1, Day = 1, Hour = 0, Minute = 0 },
            EndTime = new ScheduleTime { Year = 2025, Month = 1, Day = 15, Hour = 0, Minute = 0 },
            StarNum = 0,
            MaxFloor = "",
            BattleNum = 0,
            HasData = false,
            AllFloorDetail = null,
            MaxFloorId = 0,
            Groups =
            [
                new HsrEndGroup
                {
                    BeginTime = new ScheduleTime { Year = 2025, Month = 1, Day = 1, Hour = 0, Minute = 0 },
                    EndTime = new ScheduleTime { Year = 2025, Month = 1, Day = 15, Hour = 0, Minute = 0 },
                    Name = "Test Group"
                }
            ]
        };

        SetupMemoryApiSuccess(emptyMemoryData);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("No clear record found"));
    }

    [Test]
    public async Task ExecuteAsync_NoBattleData_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupGameDataApiSuccess();
        HsrMemoryInformation noBattleMemoryData = JsonSerializer.Deserialize<HsrMemoryInformation>(m_MemoryTestDataJson)!;
        noBattleMemoryData.BattleNum = 0;

        SetupMemoryApiSuccess(noBattleMemoryData);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("No clear record found"));
    }

    [Test]
    public async Task ExecuteAsync_UnexpectedException_LogsErrorAndSendsGenericMessage()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupGameDataApiSuccess();
        SetupMemoryApiFailure(new InvalidOperationException("Unexpected error"));

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("An error occurred"));

        // Verify error was logged
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to fetch Memory of Chaos information")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateTasksExecute_VerifyImageUpdaterCalls()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        // Verify image updater was called for each distinct avatar
        List<HsrEndAvatar> distinctAvatars = [.. m_TestMemoryData.AllFloorDetail!
            .SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
            .DistinctBy(x => x.Id)];

        foreach (HsrEndAvatar? avatar in distinctAvatars)
            m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(
                avatar.Id.ToString(), avatar.Icon), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DuplicateAvatarIds_HandlesDistinctBy()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupGameDataApiSuccess();

        // Create test data with duplicate avatar IDs
        HsrMemoryInformation memoryDataWithDuplicates = JsonSerializer.Deserialize<HsrMemoryInformation>(m_MemoryTestDataJson)!;
        if (memoryDataWithDuplicates.AllFloorDetail!.Count > 0)
        {
            FloorDetail firstFloor = memoryDataWithDuplicates.AllFloorDetail[0];
            if (firstFloor.Node1.Avatars.Count > 0)
                // Add duplicate avatar to Node2
                firstFloor.Node2.Avatars.Add(firstFloor.Node1.Avatars[0]);
        }

        SetupMemoryApiSuccess(memoryDataWithDuplicates);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        // Verify that each unique avatar ID was only updated once
        List<HsrEndAvatar> distinctAvatars = [.. memoryDataWithDuplicates.AllFloorDetail!
            .SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
            .DistinctBy(x => x.Id)];

        foreach (HsrEndAvatar? avatar in distinctAvatars)
            m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(
                avatar.Id.ToString(), avatar.Icon), Times.Once);
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
    public async Task OnAuthenticationCompletedAsync_AuthenticationSuccess_ExecutesMemoryCommand()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        // Verify success was logged
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication completed successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify HTTP calls were made
        m_HttpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region Additional Edge Case Tests

    [Test]
    public async Task ExecuteAsync_AuthenticationRequired_SendsDeferredResponse()
    {
        // Arrange
        await CreateTestUser();
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        // Verify deferred response was sent
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);

        // Verify authentication listener was registered
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            m_TestUserId, m_Executor), Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_SuccessWithDifferentContext_UpdatesContext()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        Mock<IInteractionContext> newContextMock = new();
        SlashCommandInteraction newInteraction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        newContextMock.Setup(x => x.Interaction).Returns(newInteraction);

        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, newContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        Assert.That(m_Executor.Context, Is.EqualTo(newContextMock.Object));
    }

    [Test]
    public void ExecuteAsync_InvalidParameterTypes_ThrowsCastException()
    {
        // Act & Assert
        InvalidCastException? ex = Assert.ThrowsAsync<InvalidCastException>(() =>
            m_Executor.ExecuteAsync("invalid", "parameters").AsTask());

        Assert.That(ex, Is.Not.Null);
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task FullWorkflow_SuccessfulExecution_CompletesWithoutErrors()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        // Act
        await m_Executor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        // Verify all expected HTTP calls were made
        m_HttpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());

        // Verify image updates for all distinct avatars
        List<HsrEndAvatar> distinctAvatars = [.. m_TestMemoryData.AllFloorDetail!
            .SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
            .DistinctBy(x => x.Id)];

        foreach (HsrEndAvatar? avatar in distinctAvatars)
            m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(
                avatar.Id.ToString(), avatar.Icon), Times.Once);
    }

    #endregion

    #region Helper Methods

    private async Task CreateTestUser()
    {
        UserModel userModel = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new() { ProfileId = TestProfileId }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);
    }

    private async Task CreateTestUserWithToken()
    {
        UserModel userModel = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LToken = TestLToken
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);
    }

    private async Task CreateTestUserWithCachedServer()
    {
        UserModel userModel = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.HonkaiStarRail, Regions.Asia }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);
    }

    private void SetupSuccessfulApiResponses()
    {
        SetupGameDataApiSuccess();
        SetupMemoryApiSuccess();
    }

    private void SetupGameDataApiSuccess()
    {
        // Setup successful game data API response
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
                        region = "prod_official_usa",
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
                    req.RequestUri!.GetLeftPart(UriPartial.Path).ToString() ==
                        $"{HoYoLabDomains.PublicApi}/event/game_record/card/wapi/getGameRecordCard"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(gameDataHttpResponse);
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.GetLeftPart(UriPartial.Path).ToString()
                        == $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(gameDataHttpResponse);
    }

    private void SetupMemoryApiSuccess(HsrMemoryInformation? customData = null)
    {
        HsrMemoryInformation memoryData = customData ?? m_TestMemoryData;
        var memoryResponse = new
        {
            retcode = 0,
            data = memoryData
        };

        string memoryJson = JsonSerializer.Serialize(memoryResponse);
        HttpResponseMessage memoryHttpResponse = new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(memoryJson, Encoding.UTF8, "application/json")
        };

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.GetLeftPart(UriPartial.Path).ToString() ==
                    $"{HoYoLabDomains.PublicApi}/event/game_record/hkrpg/api/challenge"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(memoryHttpResponse);
    }

    private void SetupMemoryApiFailure(Exception? exception = null)
    {
        var errorResponse = new
        {
            retcode = 1001,
            message = "API Error"
        };

        string errorJson = JsonSerializer.Serialize(errorResponse);
        HttpResponseMessage errorHttpResponse = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        };

        if (exception != null)
            m_HttpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.GetLeftPart(UriPartial.Path).ToString() ==
                        $"{HoYoLabDomains.PublicApi}/event/game_record/hkrpg/api/challenge"),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(exception);
        else
            m_HttpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.GetLeftPart(UriPartial.Path).ToString() ==
                        $"{HoYoLabDomains.PublicApi}/event/game_record/hkrpg/api/challenge"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(errorHttpResponse);
    }

    private void LoadTestData()
    {
        m_MemoryTestDataJson =
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", "Moc_TestData_1.json"));
        m_TestMemoryData = JsonSerializer.Deserialize<HsrMemoryInformation>(m_MemoryTestDataJson)!;
    }

    #endregion
}
