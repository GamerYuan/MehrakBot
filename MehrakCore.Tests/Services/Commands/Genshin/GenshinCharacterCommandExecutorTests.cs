#region

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Commands.Genshin.Character;
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
public class GenshinCharacterCommandExecutorTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";

    private GenshinCharacterCommandExecutor m_Executor = null!;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ICharacterCardService<GenshinCharacterInformation>> m_CharacterCardServiceMock = null!;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<GenshinCharacterCommandExecutor>> m_LoggerMock = null!;
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
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MongoTestHelper = new MongoTestHelper();

        // Create mocks for dependencies
        m_CharacterApiMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<GenshinCharacterInformation>>();
        m_LoggerMock = new Mock<ILogger<GenshinCharacterCommandExecutor>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Set up authentication middleware to return TestGuid
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        var imageRepository =
            new ImageRepository(m_MongoTestHelper.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(
            imageRepository,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<GenshinImageUpdaterService>>());

        // Set up mocked HTTP handler and client factory
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Create real services with mocked dependencies
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up default distributed cache behavior
        SetupDistributedCacheMock();

        // Set up interaction context
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);
        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Set up Discord test helper to capture responses
        m_DiscordTestHelper.SetupRequestCapture();

        // Create the service under test
        m_Executor = new GenshinCharacterCommandExecutor(
            m_CharacterApiMock.Object,
            m_GameRecordApiService,
            m_CharacterCardServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_UserRepository,
            m_LoggerMock.Object,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object
        )
        {
            Context = m_ContextMock.Object
        };
    }

    private void SetupDistributedCacheMock()
    {
        // Default setup for token cache - no token by default
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_WhenParametersCountInvalid_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync("param1", "param2").AsTask());
        Assert.That(ex.Message, Contains.Substring("Invalid parameters count"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoProfile_ReturnsNoProfileMessage()
    {
        // Arrange
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenProfileNotFound_ReturnsNoProfileMessage()
    {
        // Arrange
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;
        const uint profile = 2; // Non-existent profile

        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerProvidedAndNoLastUsedRegion_ReturnsNoServerMessage()
    {
        // Arrange
        const string characterName = "Traveler";
        Regions? server = null;
        const uint profile = 1;

        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found. Please select a server"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerProvidedButHasLastUsedRegion_UsesLastUsedRegion()
    {
        // Arrange
        const string characterName = "Traveler";
        Regions? server = null;
        const uint profile = 1;

        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>(),
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("auth_modal:test-guid-12345:1"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserNotAuthenticated_ShowsAuthModal()
    {
        // Arrange
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("auth_modal:test-guid-12345:1"));

        // Verify authentication middleware was called
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(TestUserId, m_Executor),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenUserIsAuthenticated_CallsSendCharacterCardResponse()
    {
        // Arrange
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        // Set up token cache to return a token
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

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
                                { server.ToString(), "800800800" }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Mock character API
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, "800800800", "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>
            {
                new()
                {
                    Id = 10000007,
                    Name = characterName,
                    Icon = "",
                    Weapon = null!
                }
            });

        var characterInfo = CreateTestCharacterInfo();
        m_CharacterApiMock.Setup(x =>
                x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, "800800800", "os_asia", 10000007))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail>
            {
                RetCode = 0,
                Data = new GenshinCharacterDetail
                {
                    List = [characterInfo],
                    AvatarWiki = new Dictionary<string, string> { { "key", "value/123" } }
                }
            });

        m_CharacterCardServiceMock.Setup(x => x.GenerateCharacterCardAsync(characterInfo, "800800800"))
            .ReturnsAsync(new MemoryStream(new byte[100])); // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("character_card.jpg").Or.Contain("Command execution completed"));

        // Verify character card was generated
        m_CharacterCardServiceMock.Verify(x => x.GenerateCharacterCardAsync(characterInfo, "800800800"), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenExceptionOccurs_SendsErrorMessage()
    {
        // Arrange
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;
        const uint profile = 1; // Create a user with a profile to avoid the "no profile" path
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = profile,
                    LtUid = TestLtUid
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Set up cache to return a token (to avoid authentication flow)
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Mock character API to throw exception
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An error occurred while processing your request"));
    }

    [Test]
    public async Task ExecuteAsync_WithNullParameters_HandlesGracefully()
    {
        // Arrange - Create a user with a profile so the null parameters flow can trigger authentication modal
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, // This matches the default profile ID when null is passed
                    LtUid = TestLtUid,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia } // Set cached server to avoid "No cached server" error
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(null, null, null);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("auth_modal:test-guid-12345:1"));
    }

    #endregion

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenAuthenticationFails_SendsErrorMessage()
    {
        // Arrange
        var authResult = AuthenticationResult.Failure(TestUserId, "Invalid passphrase");

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Authentication failed: Invalid passphrase"));
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenAuthenticationSucceedsButMissingParameters_SendsErrorMessage()
    {
        // Arrange
        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        // No pending parameters set

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Error: Missing required parameters for command execution"));
    }

    [Test]
    public async Task
        OnAuthenticationCompletedAsync_WhenAuthenticationSucceedsWithValidParameters_CallsSendCharacterCard()
    {
        // Arrange
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;

        // Set pending parameters by calling ExecuteAsync first
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
                                { server.ToString(), "800800800" }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Mock character API
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, "800800800", "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>
            {
                new()
                {
                    Id = 10000007,
                    Name = characterName,
                    Icon = "",
                    Weapon = null!
                }
            });

        var characterInfo = CreateTestCharacterInfo();
        m_CharacterApiMock.Setup(x =>
                x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, "800800800", "os_asia", 10000007))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail>
            {
                RetCode = 0,
                Data = new GenshinCharacterDetail
                {
                    List = [characterInfo],
                    AvatarWiki = new Dictionary<string, string> { { "key", "value/123" } }
                }
            });

        m_CharacterCardServiceMock.Setup(x => x.GenerateCharacterCardAsync(characterInfo, "800800800"))
            .ReturnsAsync(new MemoryStream(new byte[100]));

        // First call ExecuteAsync to set pending parameters (no token so will show auth modal)
        await m_Executor.ExecuteAsync(characterName, server, 1u); // Clear the captured request from ExecuteAsync
        m_DiscordTestHelper.ClearCapturedRequests();

        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult); // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("character_card.jpg").Or.Contain("Command execution completed"));

        // Verify character card was generated
        m_CharacterCardServiceMock.Verify(x => x.GenerateCharacterCardAsync(characterInfo, "800800800"), Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenExceptionOccurs_SendsErrorMessage()
    {
        // Arrange
        var authResult = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Create a user in the database
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Set up pending parameters by reflection
        var pendingCharacterField = typeof(GenshinCharacterCommandExecutor).GetField("m_PendingCharacterName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var pendingServerField = typeof(GenshinCharacterCommandExecutor).GetField("m_PendingServer",
            BindingFlags.NonPublic | BindingFlags.Instance);

        pendingCharacterField?.SetValue(m_Executor, "Traveler");
        pendingServerField?.SetValue(m_Executor, Regions.Asia);

        // Mock character API to throw exception
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An error occurred while processing your request"));
    }

    #endregion

    #region SendCharacterCardResponseAsync Tests

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterNotFound_ShouldSendErrorMessage()
    {
        // Arrange
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "NonExistentCharacter";
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = ltuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { server.ToString(), gameUid }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Mock character API to return empty list
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>());

        // Act
        await m_Executor.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterDetailsFail_ShouldSendErrorMessage()
    {
        // Arrange
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = ltuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { server.ToString(), gameUid }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Mock character API to return character
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>
            {
                new()
                {
                    Id = characterId,
                    Name = characterName,
                    Icon = "",
                    Weapon = null!
                }
            });

        // Mock character detail API to return authentication error
        m_CharacterApiMock.Setup(x => x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail> { RetCode = 10001 });

        // Act
        await m_Executor.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterDetailsEmpty_ShouldSendErrorMessage()
    {
        // Arrange
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = ltuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { server.ToString(), gameUid }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Mock character API to return character
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>
            {
                new()
                {
                    Id = characterId,
                    Name = characterName,
                    Icon = "",
                    Weapon = null!
                }
            });

        // Mock character detail API to return empty list
        m_CharacterApiMock.Setup(x => x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail>
            {
                RetCode = 0,
                Data = new GenshinCharacterDetail
                    { List = new List<GenshinCharacterInformation>(), AvatarWiki = new Dictionary<string, string>() }
            });

        // Act
        await m_Executor.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Failed to retrieve character data"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenAllSuccessful_ShouldSendCharacterCard()
    {
        // Arrange
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = ltuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin, new Dictionary<string, string>
                            {
                                { server.ToString(), gameUid }
                            }
                        }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Mock character API to return character
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>
            {
                new()
                {
                    Id = characterId,
                    Name = characterName,
                    Icon = "",
                    Weapon = null!
                }
            });

        // Create a character detail with required data
        var characterInfo = CreateTestCharacterInfo();

        // Mock character detail API to return valid data
        m_CharacterApiMock.Setup(x => x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail>
            {
                RetCode = 0,
                Data = new GenshinCharacterDetail
                {
                    List = [characterInfo],
                    AvatarWiki = new Dictionary<string, string> { { "key", "value/123" } }
                }
            });

        // Mock character card service
        m_CharacterCardServiceMock.Setup(x => x.GenerateCharacterCardAsync(characterInfo, gameUid))
            .ReturnsAsync(new MemoryStream(new byte[100]));

        // Act
        await m_Executor.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        // Verify the character card was generated
        m_CharacterCardServiceMock.Verify(x => x.GenerateCharacterCardAsync(characterInfo, gameUid), Times.Once);

        // Verify the image updater was called
        m_ImageUpdaterServiceMock.Verify(
            x => x.UpdateDataAsync(characterInfo, It.IsAny<IEnumerable<Dictionary<string, string>>>()), Times.Once);

        // Verify response was sent with attachment
        var fileBytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(fileBytes, Is.Not.Null);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenNoGameUidButApiSucceeds_ShouldUpdateProfileAndSendCard()
    {
        // Arrange
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile but no game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = ltuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Mock HTTP handler to return successful game UID
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                retcode = 0,
                data = new
                {
                    list = new[]
                    {
                        new { game_uid = gameUid }
                    }
                }
            }));

        // Mock character API to return character
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>
            {
                new()
                {
                    Id = characterId,
                    Name = characterName,
                    Icon = "",
                    Weapon = null!
                }
            });

        // Create a character detail with required data
        var characterInfo = CreateTestCharacterInfo();

        // Mock character detail API to return valid data
        m_CharacterApiMock.Setup(x => x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail>
            {
                RetCode = 0,
                Data = new GenshinCharacterDetail
                {
                    List = [characterInfo],
                    AvatarWiki = new Dictionary<string, string> { { "key", "value/123" } }
                }
            });

        // Mock character card service
        m_CharacterCardServiceMock.Setup(x => x.GenerateCharacterCardAsync(characterInfo, gameUid))
            .ReturnsAsync(new MemoryStream(new byte[100]));

        // Act
        await m_Executor.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        // Verify the character card was generated
        m_CharacterCardServiceMock.Verify(x => x.GenerateCharacterCardAsync(characterInfo, gameUid), Times.Once);

        // Verify user profile was updated with the game UID
        var updatedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(updatedUser?.Profiles?.First().GameUids?[GameName.Genshin][server.ToString()], Is.EqualTo(gameUid));
        Assert.That(updatedUser?.Profiles?.First().LastUsedRegions?[GameName.Genshin], Is.EqualTo(server));

        // Verify response was sent with attachment
        var fileBytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(fileBytes, Is.Not.Null);
    }

    #endregion

    #region Helper Methods

    private GenshinCharacterInformation CreateTestCharacterInfo()
    {
        return new GenshinCharacterInformation
        {
            Base = new BaseCharacterDetail
            {
                Id = 10000007,
                Name = "Traveler",
                Weapon = new Weapon
                {
                    Id = 11406,
                    Icon = "",
                    Name = "Test Weapon"
                },
                Icon = ""
            },
            Weapon = new WeaponDetail
            {
                Id = 11406,
                Name = "Test Weapon",
                Icon = "",
                TypeName = "Sword",
                MainProperty = null!
            },
            Constellations = [],
            Skills = [],
            Relics = [],
            SelectedProperties = [],
            BaseProperties = [],
            ExtraProperties = [],
            ElementProperties = []
        };
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

    #endregion
}
