#region

using System.Net;
using System.Text;
using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
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
    private readonly List<ulong> m_TestUserIds = new();
    private readonly Random m_Random = new();

    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestGameUid = "800000000";
    private const string TestLToken = "test_ltoken_value";
    private const uint TestProfileId = 1;

    private List<GenshinBasicCharacterData> m_TestCharacterList;
    private Mock<IInteractionContext> m_ContextMock;

    [SetUp]
    public void Setup()
    {
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(m_HttpClient);

        var imageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);
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

        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_DiscordTestHelper.CreateCommandInteraction(TestUserId));

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
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin", "CharList_TestData_1.json");
        var jsonContent = File.ReadAllText(testDataPath);
        var testData = JsonSerializer.Deserialize<CharacterListData>(jsonContent);
        m_TestCharacterList = testData?.List ?? new List<GenshinBasicCharacterData>();
    }

    private ulong GetUniqueUserId()
    {
        ulong userId;
        do
        {
            userId = (ulong)m_Random.Next(100000000, int.MaxValue);
        } while (m_TestUserIds.Contains(userId));

        m_TestUserIds.Add(userId);
        return userId;
    }

    private UserModel CreateTestUser(ulong userId, ulong ltUid = TestLtUid, uint profileId = TestProfileId)
    {
        return new UserModel
        {
            Id = userId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = profileId,
                    LtUid = ltUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { nameof(Regions.America), TestGameUid }
                            }
                        }
                    },
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.America }
                    }
                }
            }
        };
    }

    private UserGameData CreateTestUserGameData()
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("getUserGameRolesByLtoken")),
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
        var testUser = CreateTestUser(TestUserId);
        var testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        m_DistributedCacheMock.Verify(
            x => x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()),
            Times.Once);
        m_HttpMessageHandlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().Contains("getUserGameRolesByLtoken")),
            ItExpr.IsAny<CancellationToken>());
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"),
            Times.Once);

        var response = m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(response, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteAsync_WithNullServer_ShouldUseCachedServer()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);
        var testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
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
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithNullServerAndNoCachedServer_ShouldSendError()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);
        testUser.Profiles!.First().LastUsedRegions = null; // No cached server

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_CommandExecutor.ExecuteAsync(null, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found"));
    }

    [Test]
    public async Task ExecuteAsync_WithNonExistentUser_ShouldSendError()
    {
        var mockContext = new Mock<IInteractionContext>();
        mockContext.Setup(x => x.Interaction).Returns(m_DiscordTestHelper.CreateCommandInteraction(1u));

        m_CommandExecutor.Context = mockContext.Object;

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithInvalidProfileId_ShouldSendError()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId); // Profile ID 1

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, 999u); // Invalid profile ID

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WithNoAuthenticationToken_ShouldInitiateAuthFlow()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        m_AuthenticationMiddlewareMock.Setup(x =>
                x.RegisterAuthenticationListener(It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(Guid.NewGuid().ToString());

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(TestUserId, m_CommandExecutor),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithEmptyCharacterList_ShouldSendNoCharactersMessage()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);
        var testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>()); // Empty list

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("No characters found"));
    }

    [Test]
    public async Task ExecuteAsync_WithGameRecordApiFailure_ShouldHandleGracefully()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);

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
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Failed to retrieve game profile"));
    }

    [Test]
    public async Task ExecuteAsync_WithUnauthorizedGameRecordApi_ShouldSendAuthError()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);

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
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task ExecuteAsync_WithCommandException_ShouldLogAndSendError()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        SetupSuccessfulGameRoleApiResponse(CreateTestUserGameData());

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
            .ThrowsAsync(new CommandException("An error occurred while generating Character List card"));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
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
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
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
        var result = AuthenticationResult.Failure(800800800, "Authentication failed");

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
        var testGameData = CreateTestUserGameData();

        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { "os_usa", TestGameUid }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        var authResult = AuthenticationResult.Success(
            TestUserId,
            TestLtUid,
            TestLToken,
            m_CommandExecutor.Context);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set pending server (simulating it was set during ExecuteAsync)
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Act
        await m_CommandExecutor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"),
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
        var testUser = CreateTestUser(GetUniqueUserId());
        var testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
            .ReturnsAsync(m_TestCharacterList);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValueTask.CompletedTask);

        m_ImageUpdaterServiceMock.Setup(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Act
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert
        // Verify all services were called
        m_DistributedCacheMock.Verify(
            x => x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()),
            Times.Once);
        m_HttpMessageHandlerMock.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().Contains("getUserGameRolesByLtoken")),
            ItExpr.IsAny<CancellationToken>());
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"),
            Times.Once);

        // Verify image updates were called for all characters
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateAvatarAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeast(m_TestCharacterList.Count));
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateWeaponImageTask(It.IsAny<Weapon>()),
            Times.AtLeast(m_TestCharacterList.DistinctBy(c => c.Weapon.Id).Count()));

        // Verify Discord messages were sent
        var response = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(response, Is.Not.Null);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ExecuteAsync_WithImageUpdateFailure_ShouldSendErrorMessage()
    {
        // Arrange
        var testUser = CreateTestUser(TestUserId);
        var testGameData = CreateTestUserGameData();

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SetupSuccessfulGameRoleApiResponse(testGameData);

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "os_usa"))
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
        await m_CommandExecutor.ExecuteAsync(Regions.America, TestProfileId);

        // Assert - Should still complete and generate card
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("An error occurred while generating Character List card"));
    }

    #endregion
}
