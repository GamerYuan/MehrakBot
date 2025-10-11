#region

using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Bot.Executors.Hsr;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Hsr.Types;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Hsr.Character;
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
public class HsrCharacterCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";
    private const string TestGameUid = "800000000";

    private HsrCharacterCommandExecutor m_Executor = null!;
    private Mock<ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>> m_CharacterApiMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ICharacterCardService<HsrCharacterInformation>> m_CharacterCardServiceMock = null!;
    private Mock<ImageUpdaterService<HsrCharacterInformation>> m_ImageUpdaterServiceMock = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<HsrCharacterCommandExecutor>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private Mock<ICharacterCacheService> m_CharacterCacheServiceMock = null!;

    [SetUp]
    public void Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();

        // Create mocks for dependencies
        m_CharacterApiMock = new Mock<ICharacterApi<HsrBasicCharacterData, HsrCharacterInformation>>();
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<HsrCharacterInformation>>();
        m_LoggerMock = new Mock<ILogger<HsrCharacterCommandExecutor>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Set up authentication middleware to return TestGuid
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        ImageRepository imageRepository =
            new(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_ImageUpdaterServiceMock = new Mock<ImageUpdaterService<HsrCharacterInformation>>(
            imageRepository,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<ImageUpdaterService<HsrCharacterInformation>>>());

        // Set up mocked HTTP handler and client factory
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Create real services with mocked dependencies
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);

        m_CharacterCacheServiceMock = new Mock<ICharacterCacheService>();

        // Set up default behavior for GetAliases to return empty dictionary
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        // Set up default distributed cache behavior
        SetupDistributedCacheMock();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Set up interaction context
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Set up Discord test helper to capture responses
        m_DiscordTestHelper.SetupRequestCapture();

        // Create the service under test
        m_Executor = new HsrCharacterCommandExecutor(
            m_CharacterApiMock.Object,
            m_GameRecordApiService,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_CharacterCardServiceMock.Object,
            m_CharacterCacheServiceMock.Object,
            m_LoggerMock.Object
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
    public async Task TearDown()
    {
        m_DiscordTestHelper.Dispose();
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_WhenParametersCountInvalid_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        ArgumentException? ex = Assert.ThrowsAsync<ArgumentException>(() => m_Executor.ExecuteAsync("param1", "param2").AsTask());
        Assert.That(ex.Message, Contains.Substring("Invalid parameters count"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoProfile_ReturnsNoProfileMessage()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenProfileNotFound_ReturnsNoProfileMessage()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 2; // Non-existent profile

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = []
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerProvidedAndNoLastUsedRegion_ReturnsNoServerMessage()
    {
        // Arrange
        const string characterName = "Trailblazer";
        Regions? server = null;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = []
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found. Please select a server"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerProvidedButHasLastUsedRegion_UsesLastUsedRegion()
    {
        // Arrange
        const string characterName = "Trailblazer";
        Regions? server = null;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.HonkaiStarRail, Regions.Asia }
                    },
                    GameUids = []
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API for fetching game UID
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(Regions.Asia));

        // Setup character API
        SetupCharacterApiForSuccessfulResponse();

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Not.Contain("No cached server found"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserNotAuthenticated_ShowsAuthModal()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = []
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);
        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor),
            Times.Once);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("auth_modal"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserIsAuthenticated_CallsSendCharacterCardResponse()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser); // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup character API
        SetupCharacterApiForSuccessfulResponse();

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        m_CharacterApiMock.Verify(
            x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"), Times.Once);
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenCharacterApiThrowsException_SendsErrorMessage()
    {
        // Arrange
        UserModel user = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        [Game.HonkaiStarRail] = new()
                        {
                            ["Asia"] = TestGameUid
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup token cache to return a token (user is authenticated)
        byte[] cachedToken = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedToken);

        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(Regions.Asia));
        // Make the character API throw an exception
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new CommandException("An error occurred while retrieving character data"));

        // Act
        await m_Executor.ExecuteAsync("Stelle", Regions.Asia, 1u);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An error occurred while retrieving character data"));
    }

    [Test]
    public async Task ExecuteAsync_WithNullParameters_HandlesGracefully()
    {
        // Arrange
        string? characterName = null;
        Regions? server = Regions.Asia;
        uint? profile = null;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = []
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert - Should handle null parameters gracefully by using defaults
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Not.Contain("exception"));
    }

    #endregion

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task
        OnAuthenticationCompletedAsync_WhenAuthenticationSucceedsWithValidParameters_CallsSendCharacterCard()
    {
        // Arrange
        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { nameof(Regions.Asia), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser); // Set pending parameters
        await m_Executor.ExecuteAsync("Trailblazer", Regions.Asia, 1u);
        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(Regions.Asia));

        SetupCharacterApiForSuccessfulResponse();

        // Act
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        // Assert
        m_CharacterApiMock.Verify(
            x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"), Times.Once);
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid),
            Times.Once);
    }

    #endregion

    #region SendCharacterCardResponseAsync Tests

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterNotFound_ShouldSendErrorMessage()
    {
        // Arrange
        const string characterName = "NonExistentCharacter";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser); // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup character API to return data without the requested character
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfo();
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenNoCharacterData_ShouldSendErrorMessage()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser); // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup character API to return empty data
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([]);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No character data found"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenAllSuccessful_ShouldSendCharacterCard()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser); // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        SetupCharacterApiForSuccessfulResponse();

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);
        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid), Times.Once);
        m_ImageUpdaterServiceMock.Verify(
            x => x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                It.IsAny<IEnumerable<Dictionary<string, string>>>()), Times.Once);
        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenNoGameUidButApiSucceeds_ShouldUpdateProfileAndSendCard()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = [] // No HonkaiStarRail key initially
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes); // Setup HTTP handler to return game UID
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        SetupCharacterApiForSuccessfulResponse();

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);
        m_DiscordTestHelper.ClearCapturedRequests();

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        UserModel? updatedUser = await m_UserRepository.GetUserAsync(m_TestUserId);
        UserProfile? updatedProfile = updatedUser?.Profiles?.FirstOrDefault(x => x.ProfileId == profile);
        byte[]? bytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(updatedProfile?.GameUids, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updatedProfile?.GameUids?.ContainsKey(Game.HonkaiStarRail), Is.True);
            Assert.That(updatedProfile?.GameUids?[Game.HonkaiStarRail][server.ToString()], Is.EqualTo(TestGameUid));
            Assert.That(bytes, Is.Not.Empty);
        }
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid), Times.Once);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenGameRoleApiFails_ShouldSendErrorMessage()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = [] // No game UIDs initially
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes); // Setup HTTP handler to return invalid auth error
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateInvalidAuthGameRecordResponse()); // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Invalid HoYoLAB UID or Cookies. Please authenticate again"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenProfileRemovedDuringExecution_ShouldSendErrorMessage()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser); // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup character API to return empty list (simulates no character data)
        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([]);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No character data found. Please try again."));
    }

    #endregion

    #region Region Mapping Tests

    [Test]
    [TestCase(Regions.Asia, "prod_official_asia")]
    [TestCase(Regions.Europe, "prod_official_eur")]
    [TestCase(Regions.America, "prod_official_usa")]
    [TestCase(Regions.Sar, "prod_official_cht")]
    public async Task ExecuteAsync_ReturnsCorrectRegionMapping(Regions inputRegion, string expectedRegion)
    {
        // Arrange
        const string characterName = "Trailblazer";
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { inputRegion.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(inputRegion));

        SetupCharacterApiForSuccessfulResponse();

        // Act
        await m_Executor.ExecuteAsync(characterName, inputRegion, profile);

        // Assert
        m_CharacterApiMock.Verify(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, expectedRegion),
            Times.Once);
    }

    #endregion

    #region UpdateLastUsedRegions Tests

    [Test]
    public async Task ExecuteAsync_UpdatesLastUsedRegionsCorrectly()
    {
        // Arrange
        const string characterName = "Trailblazer";
        const Regions server = Regions.Europe;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    },
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.HonkaiStarRail, Regions.Asia } // Different from what we'll use
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache to return a token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        SetupCharacterApiForSuccessfulResponse();

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        UserModel? updatedUser = await m_UserRepository.GetUserAsync(m_TestUserId);
        UserProfile? updatedProfile = updatedUser?.Profiles?.FirstOrDefault(x => x.ProfileId == profile);
        Assert.That(updatedProfile?.LastUsedRegions?[Game.HonkaiStarRail], Is.EqualTo(server));
    }

    #endregion

    #region Alias Tests

    [Test]
    public async Task SendCharacterCardResponseAsync_WithValidAlias_ShouldFindCharacterAndSendCard()
    {
        // Arrange
        const string aliasName = "TB"; // Alias for Trailblazer
        const string actualCharacterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup alias in character cache service
        Dictionary<string, string> aliases = new()
        {
            { "TB", actualCharacterName },
            { "trailblazer", actualCharacterName }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API with a character named "Trailblazer"
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName(actualCharacterName);
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);

        // Act
        await m_Executor.ExecuteAsync(aliasName, server, profile);

        // Assert
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid), Times.Once);

        // Verify that GetAliases was called to resolve the alias
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WithCaseInsensitiveAlias_ShouldFindCharacterAndSendCard()
    {
        // Arrange
        const string aliasName = "tb"; // Lowercase alias for Trailblazer
        const string actualCharacterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup alias in character cache service (note: alias is stored as "TB"
        // but we're searching for "tb")
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TB", actualCharacterName },
            { "Stelle", actualCharacterName }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API with a character named "Trailblazer"
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName(actualCharacterName);
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);

        // Act
        await m_Executor.ExecuteAsync(aliasName, server, profile);

        // Assert
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid), Times.Once);

        // Verify that GetAliases was called to resolve the alias
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task
        SendCharacterCardResponseAsync_WithMultipleAliasesForSameCharacter_ShouldFindCharacterAndSendCard()
    {
        // Arrange
        const string aliasName = "Stelle"; // Alternative alias for Trailblazer
        const string actualCharacterName = "Trailblazer";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup multiple aliases for the same character
        Dictionary<string, string> aliases = new()
        {
            { "TB", actualCharacterName },
            { "Stelle", actualCharacterName },
            { "Caelus", actualCharacterName }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API with a character named "Trailblazer"
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName(actualCharacterName);
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);

        // Act
        await m_Executor.ExecuteAsync(aliasName, server, profile);

        // Assert
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid), Times.Once);

        // Verify that GetAliases was called
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WithSpecialCharactersInAlias_ShouldFindCharacterAndSendCard()
    {
        // Arrange
        const string aliasName = "Dr.Ratio"; // Alias with special characters
        const string actualCharacterName = "Dr. Ratio";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup alias with special characters
        Dictionary<string, string> aliases = new()
        {
            { "Dr.Ratio", actualCharacterName },
            { "Ratio", actualCharacterName }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API with a character named "Dr. Ratio"
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName(actualCharacterName);
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);

        // Act
        await m_Executor.ExecuteAsync(aliasName, server, profile);

        // Assert
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid), Times.Once);

        // Verify that GetAliases was called for each test case
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WithExactNameAndAlias_ShouldPreferExactMatch()
    {
        // Arrange
        const string characterName = "March"; // This exists as both exact character name and alias
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup alias where "March" also maps to another character
        Dictionary<string, string> aliases = new()
        {
            { "March", "March 7th" }, // "March" is an alias for "March 7th"
            { "March7", "March 7th" }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API with both "March" and "March 7th" characters
        HsrCharacterInformation exactMatchCharacter = CreateTestCharacterInfoWithName("March");
        HsrCharacterInformation aliasTargetCharacter = CreateTestCharacterInfoWithName("March 7th");

        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [exactMatchCharacter, aliasTargetCharacter],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Setup image updater service
        m_ImageUpdaterServiceMock.Setup(x =>
                x.UpdateDataAsync(It.IsAny<HsrCharacterInformation>(),
                    It.IsAny<IEnumerable<Dictionary<string, string>>>()))
            .Returns(Task.CompletedTask);

        // Setup character card service
        MemoryStream mockStream = new(Encoding.UTF8.GetBytes("mock image data"));
        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<HsrCharacterInformation>(), TestGameUid))
            .ReturnsAsync(mockStream);

        // Act
        await m_Executor.ExecuteAsync(characterName, server, profile);

        // Assert
        m_CharacterCardServiceMock.Verify(
            x => x.GenerateCharacterCardAsync(
                It.Is<HsrCharacterInformation>(c => c.Name == "March"), // Should prefer exact match
                TestGameUid),
            Times.Once);

        // GetAliases should not be called if exact match is found first
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Never);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WithInvalidAlias_ShouldSendErrorMessage()
    {
        // Arrange
        const string invalidAlias = "InvalidAlias";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup alias that doesn't include our invalid alias
        Dictionary<string, string> aliases = new()
        {
            { "TB", "Trailblazer" },
            { "Stelle", "Trailblazer" }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API that doesn't include the searched character
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName("Trailblazer"); // Different from our search
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Act
        await m_Executor.ExecuteAsync(invalidAlias, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));

        // Verify that GetAliases was called to try resolving the alias
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WithAliasPointingToNonOwnedCharacter_ShouldSendErrorMessage()
    {
        // Arrange
        const string aliasName = "FF"; // Alias for Firefly
        const string actualCharacterName = "Firefly";
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup alias that points to Firefly
        Dictionary<string, string> aliases = new()
        {
            { "FF", actualCharacterName },
            { "TB", "Trailblazer" }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API that only has Trailblazer (not Firefly)
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName("Trailblazer"); // User doesn't own Firefly
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Act
        await m_Executor.ExecuteAsync(aliasName, server, profile);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));

        // Verify that GetAliases was called to resolve the alias
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(null)]
    public async Task SendCharacterCardResponseAsync_WithEmptyOrNullAlias_ShouldHandleGracefully(string? aliasName)
    {
        // Arrange
        const Regions server = Regions.Asia;
        const uint profile = 1;

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        {
                            Game.HonkaiStarRail, new Dictionary<string, string>
                            {
                                { server.ToString(), TestGameUid }
                            }
                        }
                    }
                }
            ]
        };
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup game record API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(server));

        // Setup aliases
        Dictionary<string, string> aliases = new()
        {
            { "TB", "Trailblazer" }
        };
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        // Setup character API
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfoWithName("Trailblazer");
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);

        // Act & Assert - Should not throw an exception
        Assert.DoesNotThrowAsync(async () => await m_Executor.ExecuteAsync(aliasName, server, profile));

        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));
    }

    #endregion
    #region Helper Methods

    private static string CreateValidGameRecordResponse(Regions region = Regions.Asia)
    {
        string regionMapping = region switch
        {
            Regions.Asia => "prod_official_asia",
            Regions.Europe => "prod_official_eur",
            Regions.America => "prod_official_usa",
            Regions.Sar => "prod_official_cht",
            _ => "prod_official_asia"
        };

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
                        region = regionMapping,
                        level = 70,
                        region_name = region.ToString()
                    }
                }
            }
        };

        return JsonSerializer.Serialize(gameRecord);
    }

    private static string CreateInvalidAuthGameRecordResponse()
    {
        var gameRecord = new
        {
            retcode = -100,
            message = "Please login"
        };

        return JsonSerializer.Serialize(gameRecord);
    }

    private static HsrCharacterInformation CreateTestCharacterInfo()
    {
        return JsonSerializer.Deserialize<HsrCharacterInformation>(
            File.ReadAllText("TestData/Hsr/Stelle_TestData.json"))!;
    }

    private static HsrCharacterInformation CreateTestCharacterInfoWithName(string characterName)
    {
        HsrCharacterInformation baseCharacter = CreateTestCharacterInfo();
        return new HsrCharacterInformation
        {
            Id = baseCharacter.Id,
            Name = characterName,
            Rarity = baseCharacter.Rarity,
            Rank = baseCharacter.Rank,
            Level = baseCharacter.Level,
            Icon = baseCharacter.Icon,
            Element = baseCharacter.Element,
            Image = baseCharacter.Image,
            Equip = baseCharacter.Equip,
            Relics = baseCharacter.Relics,
            Ornaments = baseCharacter.Ornaments,
            Ranks = baseCharacter.Ranks,
            Properties = baseCharacter.Properties,
            Skills = baseCharacter.Skills,
            BaseType = baseCharacter.BaseType,
            FigurePath = baseCharacter.FigurePath,
            ElementId = baseCharacter.ElementId,
            ServantDetail = baseCharacter.ServantDetail
        };
    }

    private void SetupCharacterApiForSuccessfulResponse()
    {
        HsrCharacterInformation testCharacterData = CreateTestCharacterInfo();
        HsrBasicCharacterData characterList = new()
        {
            AvatarList = [testCharacterData],
            EquipWiki = [],
            RelicWiki = []
        };

        m_CharacterApiMock.Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, "prod_official_asia"))
            .ReturnsAsync([characterList]);
    }

    private void SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode statusCode, string content)
    {
        HttpResponseMessage response = new()
        {
            StatusCode = statusCode,
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                        req.RequestUri.GetLeftPart(UriPartial.Path).ToString() ==
                        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    #endregion
}
