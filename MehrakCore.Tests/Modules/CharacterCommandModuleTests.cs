#region

using System.Net;
using System.Text;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Rest.JsonModels;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Tests.Modules;

[Parallelizable(ParallelScope.Fixtures)]
public class CharacterCommandModuleTests
{
    private MongoTestHelper m_MongoTestHelper;
    private DiscordTestHelper m_DiscordTestHelper;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<IDistributedCache> m_DistributedCacheMock;
    private Mock<IDistributedCache> m_RateLimitCacheMock;
    private CommandRateLimitService m_RateLimitService;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiServiceMock;
    private GameRecordApiService m_GameRecordApiService;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock;
    private Mock<ICharacterCardService<GenshinCharacterInformation>> m_CharacterCardServiceMock;
    private Mock<ImageRepository> m_ImageRepositoryMock;
    private UserRepository m_UserRepository;
    private TokenCacheService m_TokenCacheService;
    private GenshinCharacterCommandService<ApplicationCommandContext> m_GenshinCommandService;
    private ServiceProvider m_ServiceProvider;
    private ApplicationCommandService<ApplicationCommandContext> m_CommandService;

    // Test constants
    private const string GoldenImagePath = "TestData/Genshin/Assets/GoldenImage.jpg";
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtuid = 987654321UL;
    private const string TestLtoken = "mock_ltoken_value";
    private const string TestCharacterName = "Traveler";
    private const string TestGameUid = "800000001";

    [SetUp]
    public async Task Setup()
    {
        // Setup MongoDB helper
        m_MongoTestHelper = new MongoTestHelper();

        // Setup Discord helper with character command
        var commandJson = new JsonApplicationCommand
        {
            Id = 123456789UL,
            Name = "character",
            Description = "Get character card",
            Type = ApplicationCommandType.ChatInput
        };
        m_DiscordTestHelper = new DiscordTestHelper(commandJson);

        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        m_CommandService.AddModule<CharacterCommandModule>();
        await m_CommandService.CreateCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Setup mocks
        SetupHttpClientMock();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_RateLimitCacheMock = new Mock<IDistributedCache>();
        m_CharacterApiServiceMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_ImageRepositoryMock = new Mock<ImageRepository>(MockBehavior.Strict, m_MongoTestHelper.MongoDbService,
            NullLogger<ImageRepository>.Instance);
        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(MockBehavior.Loose,
            m_ImageRepositoryMock.Object, m_HttpClientFactoryMock.Object,
            NullLogger<GenshinImageUpdaterService>.Instance);
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<GenshinCharacterInformation>>(MockBehavior.Loose);

        // Create a real instance of GameRecordApiService with mocked HttpClientFactory
        m_GameRecordApiService = new GameRecordApiService(m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        // Create a real instance of CommandRateLimitService with mocked dependencies
        m_RateLimitService = new CommandRateLimitService(m_RateLimitCacheMock.Object,
            NullLogger<CommandRateLimitService>.Instance);

        var imageBytes = await File.ReadAllBytesAsync(GoldenImagePath);
        m_CharacterCardServiceMock.Setup(s => s.GenerateCharacterCardAsync(
                It.IsAny<GenshinCharacterInformation>(),
                It.IsAny<string>()))
            .ReturnsAsync(() => new MemoryStream(imageBytes));

        // Setup Distributed Cache mock for TokenCacheService
        SetupDistributedCacheMock();

        // Create real repositories and services with mocked dependencies
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, Mock.Of<ILogger<TokenCacheService>>());

        // Create GenshinCharacterCommandService with mocked dependencies
        m_GenshinCommandService = new GenshinCharacterCommandService<ApplicationCommandContext>(
            m_CharacterApiServiceMock.Object,
            m_GameRecordApiService,
            m_CharacterCardServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_UserRepository,
            NullLogger<GenshinCharacterCommandService<ApplicationCommandContext>>.Instance);

        m_ServiceProvider = new ServiceCollection().AddSingleton(m_UserRepository).AddSingleton(m_GenshinCommandService)
            .AddSingleton(m_CommandService).AddSingleton(m_TokenCacheService).AddSingleton(m_RateLimitService)
            .AddLogging(l => l.AddProvider(NullLoggerProvider.Instance))
            .BuildServiceProvider();
    }

    private void SetupHttpClientMock()
    {
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();

        // Setup mock response for GetUserRegionUidAsync
        m_HttpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("getUserGameRolesByLtoken")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content =
                    new StringContent("""
                                      {
                                          "retcode": 0,
                                          "message": "OK",
                                          "data": {
                                              "list": [
                                                  {
                                                      "game_biz": "hk4e_global",
                                                      "region": "os_asia",
                                                      "game_uid": "800000001",
                                                      "nickname": "TestUser",
                                                      "level": 55
                                                  }
                                              ]
                                          }
                                      }
                                      """, Encoding.UTF8, "application/json")
            });

        // Create a HttpClient with the mocked handler
        var httpClient = new HttpClient(m_HttpMessageHandlerMock.Object);

        // Setup HttpClientFactory to return our mocked client
        m_HttpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        m_MongoTestHelper.Dispose();
        m_DiscordTestHelper.Dispose();
        m_ServiceProvider.Dispose();
    }

    private void SetupDistributedCacheMock()
    {
        // Setup token cache
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLtoken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtuid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup rate limit cache - default to no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);
    }

    [Test]
    public async Task CharacterCommand_WhenRateLimited_ReturnsRateLimitMessage()
    {
        // Arrange - Set up distributed cache to simulate rate limit
        byte[] rateBytes = Encoding.UTF8.GetBytes("true");
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rateBytes);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
        Assert.That(response, Contains.Substring("Used command too frequent!"));
    }

    [Test]
    public async Task CharacterCommand_WhenProfileDoesNotExist_ReturnsProfileNotFoundMessage()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        var user = new UserModel { Id = TestUserId }; // User with no profiles
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task CharacterCommand_WhenServerNotSelected_ReturnsNoCachedServerMessage()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtuid } // No last used region
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act - No server specified
        await ExecuteCharacterCommand(TestUserId, TestCharacterName);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found"));
    }

    [Test]
    public async Task CharacterCommand_WhenNotAuthenticated_ShowsAuthModal()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new() { ProfileId = 1, LtUid = TestLtuid }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Force cache miss for token
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtuid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Should show auth modal
        Assert.That(response, Is.Not.Null);
        Assert.That(response.ToLowerInvariant(), Contains.Substring("character_auth_modal"));
    }

    [Test]
    public async Task CharacterCommand_WhenAuthenticated_SendsCharacterCard()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Setup user with profile and game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin,
                            new Dictionary<string, string> { { nameof(Regions.Asia), TestGameUid } }
                        }
                    },
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLtoken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtuid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup character API
        var characterResponse = new ApiResult<GenshinCharacterDetail>
        {
            RetCode = 0,
            StatusCode = HttpStatusCode.Accepted,
            Data = new GenshinCharacterDetail
            {
                List =
                [
                    new GenshinCharacterInformation
                    {
                        Base = new BaseCharacterDetail
                        {
                            Id = 100000005,
                            Name = "Traveler",
                            Icon = "",
                            Weapon = null!
                        },
                        Weapon = null!,
                        Relics = [],
                        Constellations = [],
                        SelectedProperties = [],
                        BaseProperties = [],
                        ExtraProperties = [],
                        ElementProperties = [],
                        Skills = []
                    }
                ],
                AvatarWiki = []
            }
        };

        m_CharacterApiServiceMock.Setup(s => s.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([
                new GenshinBasicCharacterData
                {
                    Id = 10000005,
                    Name = "Traveler",
                    Icon = "",
                    Weapon = null!
                }
            ]);

        m_CharacterApiServiceMock.Setup(s => s.GetCharacterDataFromIdAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>()))
            .ReturnsAsync(characterResponse);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        m_CharacterApiServiceMock.Verify(s => s.GetAllCharactersAsync(
            It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        m_CharacterApiServiceMock.Verify(s => s.GetCharacterDataFromIdAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>()),
            Times.Once);

        m_CharacterCardServiceMock.Verify(s => s.GenerateCharacterCardAsync(
            It.IsAny<GenshinCharacterInformation>(), It.IsAny<string>()), Times.Once);

        var response = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        var imageBytes = await File.ReadAllBytesAsync(GoldenImagePath);

        Assert.That(response, Is.Not.Null);
        Assert.That(response, Is.EqualTo(imageBytes));
    }

    [Test]
    public async Task CharacterCommand_WhenNoGameUidFound_FetchesGameUidFromApi()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Setup user with profile but no game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtuid,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLtoken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtuid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup character API
        var characterResponse = new ApiResult<GenshinCharacterDetail>
        {
            RetCode = 0,
            StatusCode = HttpStatusCode.Accepted,
            Data = new GenshinCharacterDetail
            {
                List =
                [
                    new GenshinCharacterInformation
                    {
                        Base = new BaseCharacterDetail
                        {
                            Id = 100000005,
                            Name = "Traveler",
                            Icon = "",
                            Weapon = null!
                        },
                        Weapon = null!,
                        Relics = [],
                        Constellations = [],
                        SelectedProperties = [],
                        BaseProperties = [],
                        ExtraProperties = [],
                        ElementProperties = [],
                        Skills = []
                    }
                ],
                AvatarWiki = []
            }
        };

        m_CharacterApiServiceMock.Setup(s => s.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([
                new GenshinBasicCharacterData
                {
                    Id = 10000005,
                    Name = "Traveler",
                    Icon = "",
                    Weapon = null!
                }
            ]);

        m_CharacterApiServiceMock.Setup(s => s.GetCharacterDataFromIdAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>()))
            .ReturnsAsync(characterResponse);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        // Verify the user is updated with the new game UID
        var updatedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(updatedUser?.Profiles?.FirstOrDefault()?.GameUids, Is.Not.Null);
        Assert.That(updatedUser?.Profiles?.FirstOrDefault()?.GameUids, Contains.Key(GameName.Genshin));
        Assert.That(updatedUser?.Profiles?.FirstOrDefault()?.GameUids[GameName.Genshin],
            Contains.Key(nameof(Regions.Asia)));
    }

    [Test]
    public async Task CharacterCommand_WhenCharacterNotFound_ReturnsNotFoundMessage()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Setup user with profile and game UID
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtuid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        {
                            GameName.Genshin,
                            new Dictionary<string, string> { { nameof(Regions.Asia), TestGameUid } }
                        }
                    },
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLtoken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtuid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup character API to return empty list (character not found)
        m_CharacterApiServiceMock.Setup(s => s.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([
                new GenshinBasicCharacterData
                {
                    Id = 10000005,
                    Name = "Traveler",
                    Icon = "",
                    Weapon = null!
                }
            ]);

        // Act
        await ExecuteCharacterCommand(TestUserId, "NonexistentCharacter", Regions.Asia);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));
    }

    [Test]
    public async Task CharacterCommand_WhenErrorOccurs_ReturnsErrorMessage()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_RateLimitCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Setup user with profile
        var user = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtuid,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLtoken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtuid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Force an exception in character API
        m_CharacterApiServiceMock.Setup(s => s.GetAllCharactersAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("An error occurred"));
    }

    /// <summary>
    /// Helper method to execute character command with the necessary context setup
    /// </summary>
    private async Task ExecuteCharacterCommand(ulong userId, string characterName, Regions? server = null,
        uint profile = 1)
    {
        // Create parameters for the command
        var parameters = new List<(string, object, ApplicationCommandOptionType)>
        {
            ("character", characterName, ApplicationCommandOptionType.String)
        };

        if (server.HasValue) parameters.Add(("server", (int)server.Value, ApplicationCommandOptionType.Integer));

        if (profile != 1) parameters.Add(("profile", profile, ApplicationCommandOptionType.Integer));

        // Create interaction
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(
            userId,
            null,
            parameters.ToArray()
        );

        // Create context
        var context = new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Execute the command
        var result = await m_CommandService.ExecuteAsync(context, m_ServiceProvider);
        Assert.That(result, Is.Not.Null);
    }
}
