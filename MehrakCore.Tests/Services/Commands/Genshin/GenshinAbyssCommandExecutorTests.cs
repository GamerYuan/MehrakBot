#region

using Mehrak.Application.Services.Genshin;
using Mehrak.Application.Services.Genshin.Abyss;
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
using MehrakCore.Services.Commands.Genshin.Abyss;
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
public class GenshinAbyssCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGameUid = "800000000";
    private const uint TestFloor = 12;
    private const uint TestProfileId = 1;

    private static readonly string AccountRolesUrl =
        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken";
    private static readonly string GameRecordCardUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/card/wapi/getGameRecordCard";
    private static readonly string SpiralAbyssUrl =
        $"{HoYoLabDomains.PublicApi}/event/game_record/genshin/api/spiralAbyss";

    private GenshinAbyssCommandExecutor m_Executor = null!;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiMock = null!;
    private GenshinAbyssApiService m_ApiService = null!;
    private GenshinAbyssCardService m_CardService = null!;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock = null!;
    private UserRepository m_UserRepository = null!;
    private RedisCacheService m_TokenCacheService = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ILogger<GenshinCommandModule>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private ImageRepository m_ImageRepository = null!;

    private string m_AbyssTestDataJson = null!;

    [SetUp]
    public async Task Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();

        // Load test data
        m_AbyssTestDataJson = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin", "Abyss_TestData_1.json"));

        // Setup HTTP client mocking
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Setup distributed cache
        m_DistributedCacheMock = new Mock<IDistributedCache>(); // Create repositories and services
        m_UserRepository = new UserRepository(MongoTestHelper.Instance.MongoDbService,
            new Mock<ILogger<UserRepository>>().Object);
        m_ImageRepository = new ImageRepository(MongoTestHelper.Instance.MongoDbService,
            new Mock<ILogger<ImageRepository>>().Object);

        // Setup image assets (required for card service)
        await SetupImageAssets();

        // Setup token cache service
        m_TokenCacheService = new RedisCacheService(m_DistributedCacheMock.Object,
            new Mock<ILogger<RedisCacheService>>().Object);

        // Setup GameRecord API service
        m_GameRecordApiService = new GameRecordApiService(m_HttpClientFactoryMock.Object,
            new Mock<ILogger<GameRecordApiService>>().Object);
        m_ApiService = new GenshinAbyssApiService(m_HttpClientFactoryMock.Object,
            NullLogger<GenshinAbyssApiService>.Instance);
        m_CardService = new GenshinAbyssCardService(m_ImageRepository,
            NullLogger<GenshinAbyssCardService>.Instance);

        await m_CardService.InitializeAsync();

        // Setup mocks
        m_CharacterApiMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(
            m_ImageRepository, m_HttpClientFactoryMock.Object,
            NullLogger<GenshinImageUpdaterService>.Instance);

        // Setup virtual method mocks for ImageUpdaterService
        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);
        m_ImageUpdaterServiceMock.Setup(x => x.UpdateSideAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_LoggerMock = new Mock<ILogger<GenshinCommandModule>>();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Setup Discord interaction
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId, "abyss",
            ("floor", TestFloor, ApplicationCommandOptionType.Integer),
            ("server", "america", ApplicationCommandOptionType.String),
            ("profile", TestProfileId, ApplicationCommandOptionType.Integer));

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        m_Executor = new GenshinAbyssCommandExecutor(
            m_CardService,
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

        // Setup Discord response capture
        m_DiscordTestHelper.SetupRequestCapture();
    }

    [TearDown]
    public async Task TearDown()
    {
        m_DiscordTestHelper.Dispose();
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
    }

    private async Task SetupImageAssets()
    {
        // Load images from main Assets directory
        foreach (string image in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets"), "*.png",
                     SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(image).Split('.')[0];
            if (await m_ImageRepository.FileExistsAsync(fileName)) continue;

            await using FileStream stream = File.OpenRead(image);
            await m_ImageRepository.UploadFileAsync(fileName, stream);
        }
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_WithInvalidParameterCount_ThrowsArgumentException()
    {
        // Arrange
        object[] parameters = [TestFloor, Server.America]; // Missing profile parameter

        // Act & Assert
        ArgumentException? exception = Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync(parameters).AsTask());

        Assert.That(exception.Message, Does.Contain("Invalid number of parameters"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentUser_ReturnsEarly()
    {
        // Arrange
        object[] parameters = [TestFloor, Server.America, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters); // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidProfile_ReturnsEarly()
    {
        // Arrange
        await CreateTestUserWithProfile();
        object[] parameters = [TestFloor, Server.America, 999u]; // Invalid profile ID

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithNoServerAndNoCachedServer_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfileNoServer();
        object?[] parameters = [TestFloor, null, TestProfileId]; // No server specified

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("No cached server found! Please select a server first."));
    }

    [Test]
    public async Task ExecuteAsync_WithValidParametersAndCachedToken_GeneratesAbyssCard()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupSuccessfulApiResponses();
        await SetupCachedToken();

        object[] parameters = [TestFloor, Server.America, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("Abyss Summary"));

        // Verify API calls were made
        VerifyHttpRequest(AccountRolesUrl, Times.Once());
        VerifyHttpRequest(SpiralAbyssUrl, Times.Once());
    }

    [Test]
    public async Task ExecuteAsync_WithValidParametersButNoToken_RequestsAuthentication()
    {
        // Arrange
        await CreateTestUserWithProfile();
        object[] parameters = [TestFloor, Server.America, TestProfileId];

        // Setup token cache to return null (no cached token)
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert Verify authentication was requested
        m_AuthenticationMiddlewareMock.Verify(
            x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor),
            Times.Once);
    }

    #endregion

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithFailedAuthentication_LogsError()
    {
        // Arrange
        AuthenticationResult failureResult = AuthenticationResult.Failure(m_TestUserId, "Authentication failed");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(failureResult);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulAuthentication_GeneratesAbyssCard()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupSuccessfulApiResponses();

        AuthenticationResult successResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(successResult);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("Abyss Summary"));
    }

    #endregion

    #region API Response Tests

    [Test]
    public async Task ExecuteAsync_WithApiFailure_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();
        SetupHttpResponse(GameRecordCardUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);
        SetupHttpResponse(SpiralAbyssUrl,
            "", HttpStatusCode.InternalServerError);

        object[] parameters = [TestFloor, Server.America, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("error"));
    }

    [Test]
    public async Task ExecuteAsync_WithNoFloorData_SendsNoRecordMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();
        await SetupSuccessfulApiResponses();

        // Setup abyss response with no floor 12 data
        string abyssDataWithoutFloor12 = CreateAbyssDataWithoutTargetFloor();
        SetupHttpResponse(SpiralAbyssUrl,
            abyssDataWithoutFloor12, HttpStatusCode.OK);

        object[] parameters = [TestFloor, Server.America, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("No clear record found for floor 12."));
    }

    [Test]
    public async Task ExecuteAsync_WithNoCharacters_SendsErrorMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();
        await SetupSuccessfulApiResponses();

        // Setup character API to return empty list
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        object[] parameters = new object[] { TestFloor, Server.America, TestProfileId };

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("Failed to fetch character list. Please try again later."));
    }

    #endregion

    #region Helper Methods

    private async Task CreateTestUserWithProfile()
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
                    LastUsedRegions = new Dictionary<Game, Server>
                    {
                        { Game.Genshin, Server.America }
                    }
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private async Task CreateTestUserWithProfileNoServer()
    {
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid
                    // No LastUsedRegions to simulate no cached server
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }

    private Task SetupCachedToken()
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);
        return Task.CompletedTask;
    }

    private Task SetupSuccessfulApiResponses()
    {
        // Setup GameRecord API response
        SetupHttpResponse(GameRecordCardUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);

        // Setup User Game Roles API response (needed for GetUserGameDataAsync)
        SetupHttpResponse(AccountRolesUrl,
            CreateValidGameRecordResponse(), HttpStatusCode.OK);

        // Setup Abyss API response
        SetupHttpResponse(SpiralAbyssUrl,
            CreateValidAbyssResponse(), HttpStatusCode.OK);

        // Setup character API response
        List<GenshinBasicCharacterData> characters =
        [
            new()
            {
                Id = 10000032,
                ActivedConstellationNum = 6,
                Icon = "https://example.com/icon1.png",
                Name = "Bennett",
                Weapon = new Weapon
                {
                    Id = 11501,
                    Icon = "https://example.com/weapon1.png",
                    Name = "Test Weapon 1"
                }
            },
            new()
            {
                Id = 10000037,
                ActivedConstellationNum = 1,
                Icon = "https://example.com/icon2.png",
                Name = "Ganyu",
                Weapon = new Weapon
                {
                    Id = 11502,
                    Icon = "https://example.com/weapon2.png",
                    Name = "Test Weapon 2"
                }
            },
            new()
            {
                Id = 10000063,
                ActivedConstellationNum = 2,
                Icon = "https://example.com/icon3.png",
                Name = "Shenhe",
                Weapon = new Weapon
                {
                    Id = 11503,
                    Icon = "https://example.com/weapon3.png",
                    Name = "Test Weapon 3"
                }
            },
            new()
            {
                Id = 10000089,
                ActivedConstellationNum = 6,
                Icon = "https://example.com/icon4.png",
                Name = "Furina",
                Weapon = new Weapon
                {
                    Id = 11504,
                    Icon = "https://example.com/weapon4.png",
                    Name = "Test Weapon 4"
                }
            },
            new()
            {
                Id = 10000103,
                ActivedConstellationNum = 3,
                Icon = "https://example.com/icon5.png",
                Name = "Xilonen",
                Weapon = new Weapon
                {
                    Id = 11505,
                    Icon = "https://example.com/weapon5.png",
                    Name = "Test Weapon 5"
                }
            },
            new()
            {
                Id = 10000106,
                ActivedConstellationNum = 0,
                Icon = "https://example.com/icon6.png",
                Name = "Mavuika",
                Weapon = new Weapon
                {
                    Id = 11506,
                    Icon = "https://example.com/weapon6.png",
                    Name = "Test Weapon 6"
                }
            },
            new()
            {
                Id = 10000107,
                ActivedConstellationNum = 4,
                Icon = "https://example.com/icon7.png",
                Name = "Citlali",
                Weapon = new Weapon
                {
                    Id = 11507,
                    Icon = "https://example.com/weapon7.png",
                    Name = "Test Weapon 7"
                }
            },
            new()
            {
                Id = 10000112,
                ActivedConstellationNum = 5,
                Icon = "https://example.com/icon8.png",
                Name = "Escoffier",
                Weapon = new Weapon
                {
                    Id = 11508,
                    Icon = "https://example.com/weapon8.png",
                    Name = "Test Weapon 8"
                }
            }
        ];
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(characters);

        return Task.CompletedTask;
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

    private void VerifyHttpRequest(string url, Times times)
    {
        m_HttpMessageHandlerMock.Protected()
            .Verify("SendAsync", times,
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.GetLeftPart(UriPartial.Path) == url),
                ItExpr.IsAny<CancellationToken>());
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

    private string CreateValidAbyssResponse()
    {
        var abyssResponse = new
        {
            retcode = 0,
            message = "OK",
            data = JsonSerializer.Deserialize<object>(m_AbyssTestDataJson)
        };

        return JsonSerializer.Serialize(abyssResponse);
    }

    private string CreateAbyssDataWithoutTargetFloor()
    {
        Dictionary<string, object> modifiedData = JsonSerializer.Deserialize<Dictionary<string, object>>(m_AbyssTestDataJson)!;

        // Remove the target floor from floors array
        if (modifiedData.ContainsKey("floors")) modifiedData["floors"] = Array.Empty<object>(); // Empty floors array

        var abyssResponse = new
        {
            retcode = 0,
            message = "OK",
            data = modifiedData
        };

        return JsonSerializer.Serialize(abyssResponse);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ExecuteAsync_WhenExceptionOccurs_LogsErrorAndSendsGenericMessage()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupCachedToken();

        // Setup HTTP response to throw exception
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        object[] parameters = new object[] { TestFloor, Server.America, TestProfileId };

        // Act
        await m_Executor.ExecuteAsync(parameters); // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error generating Abyss card for floor")),

                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(responseMessage, Does.Contain("error occurred"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenExceptionOccurs_LogsError()
    {
        // Arrange Create user with matching ltuid so the flow continues past
        // profile validation
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = TestProfileId,
                    LtUid = TestLtUid, // This matches the TestLtUid used in AuthenticationResult
                    LastUsedRegions = new Dictionary<Game, Server>
                    {
                        { Game.Genshin, Server.America }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        AuthenticationResult successResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Setup to throw exception during HTTP processing
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(successResult); // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Error generating Abyss card for floor")),

                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task ExecuteAsync_FullWorkflow_GeneratesCompleteAbyssCard()
    {
        // Arrange
        await CreateTestUserWithProfile();
        await SetupSuccessfulApiResponses();
        await SetupCachedToken();

        object[] parameters = [TestFloor, Server.America, TestProfileId];

        // Act
        await m_Executor.ExecuteAsync(parameters);

        // Assert
        string responseMessage = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the response contains expected elements
        Assert.That(responseMessage, Contains.Substring("Abyss Summary").IgnoreCase);
        Assert.That(responseMessage, Contains.Substring("Floor 12").IgnoreCase);
        Assert.That(responseMessage, Contains.Substring("Cycle start").IgnoreCase);
        Assert.That(responseMessage, Contains.Substring("Cycle end").IgnoreCase);

        // Verify all expected API calls were made
        VerifyHttpRequest(AccountRolesUrl, Times.Once());
        VerifyHttpRequest(SpiralAbyssUrl, Times.Once()); // Verify character API was called
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(
            TestLtUid, TestLToken, TestGameUid, "os_usa"), Times.Once);
    }

    #endregion
}
