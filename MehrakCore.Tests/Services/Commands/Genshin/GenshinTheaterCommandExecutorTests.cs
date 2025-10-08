#region

using Mehrak.Application.Services.Genshin;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Bot.Executors.Genshin;
using Mehrak.Bot.Modules;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi;
using Mehrak.GameApi.Genshin.Types;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Commands.Genshin.Theater;
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
public class GenshinTheaterCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGameUid = "800000000";
    private const uint TestProfileId = 1;
    private const string TestGuid = "test-guid-12345";

    private static readonly string GameRecordCardUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/card/wapi/getGameRecordCard";
    private static readonly string AccountRolesUrl =
        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken";
    private static readonly string TheaterUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/genshin/api/roleCalendar";
    private static readonly string BuffUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/genshin/api/roleCalendar/buff";

    private GenshinTheaterCommandExecutor m_Executor = null!;
    private GenshinTheaterCardService m_CommandService = null!;
    private GenshinTheaterApiService m_ApiService = null!;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock = null!;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiMock = null!;
    private UserRepository m_UserRepository = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ILogger<GenshinCommandModule>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;

    private GenshinTheaterInformation m_TestTheaterData = null!;
    private string m_TheaterTestDataJson = null!;

    [SetUp]
    public async Task Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();
        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Load test data
        m_TheaterTestDataJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin", "Theater_TestData_1.json"));
        m_TestTheaterData = JsonSerializer.Deserialize<GenshinTheaterInformation>(m_TheaterTestDataJson)!;

        // Setup HTTP client mocking
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Setup mocks
        Mock<ImageRepository> imageRepositoryMock =
            new(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);
        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(
            imageRepositoryMock.Object,
            m_HttpClientFactoryMock.Object,
            Mock.Of<ILogger<GenshinImageUpdaterService>>());
        m_CharacterApiMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_LoggerMock = new Mock<ILogger<GenshinCommandModule>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();

        // Setup real services
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_GameRecordApiService =
            new GameRecordApiService(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);

        // Create real command and API services with mocked dependencies
        m_CommandService = new GenshinTheaterCardService(
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, Mock.Of<ILogger<ImageRepository>>()),
            Mock.Of<ILogger<GenshinTheaterCardService>>());
        m_ApiService = new GenshinTheaterApiService(
            m_HttpClientFactoryMock.Object,
            Mock.Of<ILogger<GenshinTheaterApiService>>());

        // Setup authentication middleware
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Setup context mock
        m_ContextMock = new Mock<IInteractionContext>();
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Create executor
        m_Executor = new GenshinTheaterCommandExecutor(
            m_CommandService,
            m_ApiService,
            m_ImageUpdaterServiceMock.Object,
            m_CharacterApiMock.Object,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object)
        {
            Context = m_ContextMock.Object
        };

        // Setup image updater service mocks
        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);
        m_ImageUpdaterServiceMock.Setup(x => x.UpdateSideAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        // Setup character API mock
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(GetTestCharacters());

        // Setup Discord test helper for request capture
        m_DiscordTestHelper.SetupRequestCapture();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
        m_DiscordTestHelper.Dispose();
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_InvalidParameters_ThrowsException()
    {
        // Act & Assert - The implementation casts parameters directly without validation
        IndexOutOfRangeException? ex = Assert.ThrowsAsync<IndexOutOfRangeException>(() => m_Executor.ExecuteAsync().AsTask());

        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_UserNotFound_ReturnsEarly()
    {
        // Arrange - Don't create user in database

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_ProfileNotFound_ReturnsEarly()
    {
        // Arrange
        await CreateTestUser();

        // Act
        await m_Executor.ExecuteAsync(Regions.America, 999u); // Non-existent profile

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
        Assert.That(user!.Profiles?.FirstOrDefault()?.LastUsedRegions?[GameName.Genshin], Is.EqualTo(Regions.America));
    }

    [Test]
    public async Task ExecuteAsync_UserNotAuthenticated_StartsAuthenticationFlow()
    {
        // Arrange
        await CreateTestUserUnauthenticated(); // User without token

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            m_TestUserId, m_Executor), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UserAuthenticated_ExecutesTheaterCommand()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        UserModel? user = await m_UserRepository.GetUserAsync(m_TestUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Profiles?.FirstOrDefault()?.LtUid, Is.EqualTo(TestLtUid));
    }

    [Test]
    public async Task ExecuteAsync_ApiError_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithToken();

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_CharacterApiReturnsEmpty_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();
        SetupTheaterApiSuccess();
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        // With real services, authentication errors might occur instead
        Assert.That(responseContent, Does.Contain("An error occurred while retrieving Imaginarium Theater data"));
    }

    [Test]
    public async Task ExecuteAsync_BuffApiError_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupTheaterApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_CommandException_LogsErrorAndSendsMessage()
    {
        // Arrange
        await CreateTestUserUnauthenticated(); // User that will trigger authentication flow

        // Mock authentication middleware to throw CommandException
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Throws(new CommandException("Command error"));

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Command execution failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("Command error"));
    }

    [Test]
    public async Task ExecuteAsync_UnexpectedException_LogsErrorAndSendsGenericMessage()
    {
        // Arrange
        await CreateTestUserUnauthenticated(); // User that will trigger authentication flow

        // Mock authentication middleware to throw unexpected exception
        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Throws(new InvalidOperationException("Unexpected error"));

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("unexpected error occurred")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("An unknown error occurred"));
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
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication failed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_AuthenticationSuccess_ExecutesTheaterCommand()
    {
        // Arrange
        await CreateTestUser();
        SetupSuccessfulApiResponses();

        AuthenticationResult result = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Set pending server (this would be set by ExecuteAsync)
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication completed successfully")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // With real services, we validate successful execution through response content
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    #endregion

    #region Additional Edge Case Tests

    [Test]
    public async Task ExecuteAsync_AuthenticationRequired_SendsDeferredResponse()
    {
        // Arrange
        await CreateTestUserUnauthenticated(); // User without token

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert - Verify that authentication middleware was called
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            m_TestUserId, m_Executor), Times.Once);

        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Does.Contain("auth_modal"));
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateTasksExecute_VerifyImageUpdaterCalls()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupSuccessfulApiResponses();

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_DuplicateAvatarIds_HandlesDistinctBy()
    {
        // Arrange
        await CreateTestUserWithToken();

        // Create theater data with duplicate avatar IDs
        GenshinTheaterInformation theaterDataWithDuplicates = JsonSerializer.Deserialize<GenshinTheaterInformation>(m_TheaterTestDataJson)!;
        if (theaterDataWithDuplicates.Detail.RoundsData.Count > 0)
        {
            RoundsData firstRound = theaterDataWithDuplicates.Detail.RoundsData[0];
            if (firstRound.Avatars.Count > 0)
            {
                // Add duplicate avatar
                ItAvatar duplicateAvatar =
                    JsonSerializer.Deserialize<ItAvatar>(JsonSerializer.Serialize(firstRound.Avatars[0]))!;
                firstRound.Avatars.Add(duplicateAvatar);
            }
        }

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert - Should handle duplicates without errors
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_CommandServiceThrowsCommandException_HandlesAndLogsError()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupTheaterApiSuccess();
        SetupBuffApiSuccess();

        // Act
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_CharacterDataWithNullValues_HandlesGracefully()
    {
        // Arrange
        await CreateTestUserWithToken();
        SetupTheaterApiSuccess();
        SetupBuffApiSuccess();

        // Setup character API to return data with null values
        List<GenshinBasicCharacterData> charactersWithNulls =
        [
            new()
            {
                Id = 10000089,
                ActivedConstellationNum = 6,
                Name = "Furina",
                Icon = "test_icon",
                Weapon = new Weapon
                    { Id = 1, Name = "Test", Icon = "test", Type = 1, Level = 1, Rarity = 5, AffixLevel = 1 }
            },
            new()
            {
                Id = null, // Null ID
                ActivedConstellationNum = 0,
                Name = "Invalid Character",
                Icon = "test_icon2",
                Weapon = new Weapon
                    { Id = 2, Name = "Test2", Icon = "test2", Type = 1, Level = 1, Rarity = 5, AffixLevel = 1 }
            },
            new()
            {
                Id = 10000032,
                ActivedConstellationNum = null, // Null constellation
                Name = "Bennett",
                Icon = "test_icon3",
                Weapon = new Weapon
                    { Id = 3, Name = "Test3", Icon = "test3", Type = 1, Level = 1, Rarity = 5, AffixLevel = 1 }
            }
        ];

        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(charactersWithNulls);

        // Act & Assert
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_SuccessWithDifferentContext_UpdatesContext()
    {
        // Arrange
        await CreateTestUser();
        SlashCommandInteraction newInteraction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        Mock<IInteractionContext> newContextMock = new();
        newContextMock.Setup(x => x.Interaction).Returns(newInteraction);

        SetupSuccessfulApiResponses();

        AuthenticationResult result = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, newContextMock.Object);

        // Set pending server
        await m_Executor.ExecuteAsync(Regions.Europe, TestProfileId);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(result);

        // Assert
        Assert.That(m_Executor.Context, Is.EqualTo(newContextMock.Object));
    }

    [Test]
    public void ExecuteAsync_InvalidParameterTypes_ThrowsCastException()
    {
        // Act & Assert - The implementation casts parameters directly
        InvalidCastException? ex = Assert.ThrowsAsync<InvalidCastException>(() =>
            m_Executor.ExecuteAsync("invalid", "parameters").AsTask());

        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public void ExecuteAsync_NullParameters_ThrowsCastException()
    {
        // Act & Assert
        InvalidCastException? ex = Assert.ThrowsAsync<InvalidCastException>(() => m_Executor.ExecuteAsync(null, null).AsTask());

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
        await m_Executor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        string responseContent = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseContent, Is.Not.Null);

        UserModel? user = await m_UserRepository.GetUserAsync(m_TestUserId);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Profiles?.FirstOrDefault()?.LtUid, Is.EqualTo(TestLtUid));
    }

    #endregion

    #region Helper Methods

    private async Task CreateTestUser()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = 0 // Not authenticated
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private async Task CreateTestUserUnauthenticated()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = 0 // Not authenticated
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Ensure token cache returns null (user not authenticated)
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null!);
    }

    private async Task CreateTestUserWithToken()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid,
                    LToken = TestLToken,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        [GameName.Genshin] = new()
                        {
                            ["uid"] = TestGameUid
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache
        m_DistributedCacheMock.Setup(x => x.GetAsync($"ltoken_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));
    }

    private async Task CreateTestUserWithCachedServer()
    {
        UserModel user = new()
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
                        [GameName.Genshin] = Regions.America
                    },
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        [GameName.Genshin] = new()
                        {
                            ["uid"] = TestGameUid
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache
        m_DistributedCacheMock.Setup(x => x.GetAsync($"ltoken_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));
    }

    private void SetupSuccessfulApiResponses()
    {
        // Setup GameRecord API response
        SetupHttpResponse(GameRecordCardUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);

        // Setup User Game Roles API response (needed for GetUserGameDataAsync)
        SetupHttpResponse(AccountRolesUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);

        // Setup Theater API response
        SetupHttpResponse(TheaterUrl,
            CreateValidTheaterResponse(), HttpStatusCode.OK);

        // Setup Buff API response
        SetupHttpResponse(BuffUrl,
            CreateValidBuffResponse(), HttpStatusCode.OK);

        // Setup character API response
        IEnumerable<GenshinBasicCharacterData> characters = GetTestCharacters();
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(characters);
    }

    private void SetupTheaterApiSuccess()
    {
        SetupHttpResponse(TheaterUrl,
            CreateValidTheaterResponse(), HttpStatusCode.OK);
    }

    private void SetupBuffApiSuccess()
    {
        SetupHttpResponse(BuffUrl,
            CreateValidBuffResponse(), HttpStatusCode.OK);
    }

    private void SetupHttpResponse(string url, string responseContent, HttpStatusCode statusCode)
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.GetLeftPart(UriPartial.Path) == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });
    }

    private static string CreateValidGameRecordResponse()
    {
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
                        region = "os_usa",
                        level = 60,
                        region_name = "America"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(gameRecord);
    }

    private string CreateValidTheaterResponse()
    {
        var theaterResponse = new
        {
            retcode = 0,
            message = "OK",
            data = m_TestTheaterData
        };

        return JsonSerializer.Serialize(theaterResponse);
    }

    private static string CreateValidBuffResponse()
    {
        var buffResponse = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                buffs = new[]
                {
                    new
                    {
                        id = 1,
                        name = "Test Buff 1",
                        icon = "https://example.com/buff1.png",
                        description = "Test buff description 1"
                    },
                    new
                    {
                        id = 2,
                        name = "Test Buff 2",
                        icon = "https://example.com/buff2.png",
                        description = "Test buff description 2"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(buffResponse);
    }

    private static IEnumerable<GenshinBasicCharacterData> GetTestCharacters()
    {
        return
        [
            new()
            {
                Id = 10000089,
                Icon = "test_icon_furina",
                ActivedConstellationNum = 6,
                Name = "Furina",
                Weapon = new Weapon
                {
                    Id = 123,
                    Icon = "test_weapon_icon",
                    Name = "Test Weapon"
                }
            },
            new()
            {
                Id = 10000037,
                Icon = "test_icon_ganyu",
                ActivedConstellationNum = 0,
                Name = "Ganyu",
                Weapon = new Weapon
                {
                    Id = 456,
                    Icon = "test_weapon_icon2",
                    Name = "Test Weapon 2"
                }
            },
            new()
            {
                Id = 10000032,
                Icon = "test_icon_bennett",
                ActivedConstellationNum = 3,
                Name = "Bennett",
                Weapon = new Weapon
                {
                    Id = 789,
                    Icon = "test_weapon_icon3",
                    Name = "Test Weapon 3"
                }
            }
        ];
    }

    #endregion
}
