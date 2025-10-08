#region

using Mehrak.Application.Services.Genshin;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Bot.Executors.Genshin;
using Mehrak.Bot.Modules;
using Mehrak.Domain.Interfaces;
using Mehrak.GameApi;
using Mehrak.GameApi.Common.ApiResponseTypes;
using Mehrak.GameApi.Genshin.Types;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Commands.Genshin.CharList;
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

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharListCommandExecutorTests
{
    private GenshinCharListCommandExecutor m_CommandExecutor;
    private Mock<GenshinCharListCardService> m_CommandServiceMock;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiMock;
    private UserRepository m_UserRepository;
    private Mock<TokenCacheService> m_TokenCacheServiceMock;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock;
    private Mock<GameRecordApiService> m_GameRecordApiMock;
    private Mock<ILogger<GenshinCommandModule>> m_LoggerMock;
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

    private List<GenshinBasicCharacterData> m_TestCharacterList;
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
        m_CommandServiceMock = new Mock<GenshinCharListCardService>(
            imageRepository,
            NullLogger<GenshinCharListCardService>.Instance);

        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(

            imageRepository,
            m_HttpClientFactoryMock.Object,
            NullLogger<GenshinImageUpdaterService>.Instance);

        m_DistributedCacheMock = new Mock<IDistributedCache>();

        m_CharacterApiMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheServiceMock =
            new Mock<TokenCacheService>(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();
        m_GameRecordApiMock =
            new Mock<GameRecordApiService>(m_HttpClientFactoryMock.Object, NullLogger<GameRecordApiService>.Instance);
        m_LoggerMock = new Mock<ILogger<GenshinCommandModule>>();

        m_DiscordTestHelper = new DiscordTestHelper();

        // Load test character data
        LoadTestCharacterData();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId));

        // Create the command executor
        m_CommandExecutor = new GenshinCharListCommandExecutor(
            m_CommandServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_CharacterApiMock.Object,
            m_UserRepository,
            m_TokenCacheServiceMock.Object,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiMock.Object,
            m_LoggerMock.Object
        )
        {
            Context = m_ContextMock.Object
        };
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_HttpClient.Dispose();
    }

    private void LoadTestCharacterData()
    {
        string testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin", "CharList_TestData_1.json");
        string jsonContent = File.ReadAllText(testDataPath);
        CharacterListData? testData = JsonSerializer.Deserialize<CharacterListData>(jsonContent);
        m_TestCharacterList = testData?.List ?? [];
    }

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
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { nameof(Regions.Asia), TestGameUid }
                            }
                        }
                    },
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            ]
        };
    }

    private static UserGameData CreateTestUserGameData()
    {
        return new UserGameData
        {
            GameUid = TestGameUid,
            Level = 60,
            Nickname = "TestUser"
        };
    }

    private void SetupSuccessfulGameRoleApiResponse(UserGameData testGameData)
    {
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            retcode = 0,
            data = new
            {
                list = new object[]
                {
                    testGameData
                }
            }
        }));
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

    #region ExecuteAsync Tests

    [Test]
    public async Task ExecuteAsync_WithValidParametersAndCachedServer_ShouldExecuteSuccessfully()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);
        UserGameData testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_DistributedCacheMock.Verify(
            x => x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()),
            Times.Once);
        m_HttpMessageHandlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(x => x.RequestUri != null &&
                                               x.RequestUri!.GetLeftPart(UriPartial.Path) == AccountRolesUrl),
            ItExpr.IsAny<CancellationToken>());
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"),
            Times.Once);

        Task<byte[]?> response = m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_WithNullServer_ShouldUseCachedServer()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);
        UserGameData testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(null, TestProfileId);

        // Assert - Should use cached server (America)
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithNullServerAndNoCachedServer_ShouldSendError()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);
        testUser.Profiles!.First().LastUsedRegions = null; // No cached server

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_CommandExecutor.ExecuteAsync(null, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentUser_ShouldSendError()
    {
        Mock<IInteractionContext> mockContext = new();
        mockContext.Setup(x => x.Interaction).Returns(m_DiscordTestHelper.CreateCommandInteraction(1u));

        m_CommandExecutor.Context = mockContext.Object;

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidProfileId_ShouldSendError()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId); // Profile ID 1

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, 999u); // Invalid profile ID

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithNoAuthenticationToken_ShouldInitiateAuthFlow()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(Guid.NewGuid().ToString());

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(m_TestUserId, m_CommandExecutor),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithEmptyCharacterList_ShouldSendNoCharactersMessage()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);
        UserGameData testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ReturnsAsync([]); // Empty list

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No characters found"));
    }

    [Test]
    public async Task ExecuteAsync_WithGameRecordApiFailure_ShouldHandleGracefully()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new
        {
            retcode = -100,
            message = "API Error"
        }));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Failed to retrieve game profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithUnauthorizedGameRecordApi_ShouldSendAuthError()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            retcode = -100,
            data = "Please login"
        }));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task ExecuteAsync_WithCommandException_ShouldLogAndSendError()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        SetupSuccessfulGameRoleApiResponse(CreateTestUserGameData());

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ThrowsAsync(new CommandException("An error occurred while generating Character List card"));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while generating Character List card"));

        // Verify logging
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to get Character List card")),

                It.IsAny<CommandException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithUnexpectedException_ShouldLogAndSendGenericError()
    {
        UserModel testUser = CreateTestUser(m_TestUserId);
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while updating user data"));

        // Verify logging
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to get Character List card")),

                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_AuthenticationFailed_LogsError()
    {
        // Arrange
        AuthenticationResult result = AuthenticationResult.Failure(800800800, "Authentication failed");

        // Act
        await m_CommandExecutor.OnAuthenticationCompletedAsync(result);

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
    public async Task OnAuthenticationCompletedAsync_WithSuccessfulAuth_ShouldExecuteCharListCard()
    {
        // Arrange
        UserGameData testGameData = CreateTestUserGameData();

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { "os_asia", TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        AuthenticationResult authResult = AuthenticationResult.Success(
            m_TestUserId,
            TestLtUid,
            TestLToken,
            m_CommandExecutor.Context);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set pending server (simulating it was set during ExecuteAsync)
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Act
        await m_CommandExecutor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"),
            Times.AtLeastOnce);

        // Verify success logging
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication completed successfully")),

                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task FullWorkflow_WithValidData_ShouldCompleteSuccessfully()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);
        UserGameData testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert
        // Verify all services were called
        m_DistributedCacheMock.Verify(
            x => x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()),
            Times.Once);
        m_HttpMessageHandlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(x => x.RequestUri != null &&
                                               x.RequestUri!.GetLeftPart(UriPartial.Path) == AccountRolesUrl),
            ItExpr.IsAny<CancellationToken>());
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"),
            Times.Once);

        // Verify image updates were called for all characters
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeast(m_TestCharacterList.Count));
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()),
            Times.AtLeast(m_TestCharacterList.DistinctBy(c => c.Weapon.Id).Count()));

        // Verify Discord messages were sent
        byte[]? response = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(response, Is.Not.Null);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ExecuteAsync_WithImageUpdateFailure_ShouldSendErrorMessage()
    {
        // Arrange
        UserModel testUser = CreateTestUser(m_TestUserId);
        UserGameData testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_asia"))
            .ReturnsAsync(m_TestCharacterList);

        // Setup some image updates to fail
        m_ImageUpdaterServiceMock.SetupSequence(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask)
            .Throws<HttpRequestException>() // This should not stop execution
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.Asia, TestProfileId);

        // Assert - Should still complete and generate card
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while generating Character List card"));
    }

    #endregion
}
