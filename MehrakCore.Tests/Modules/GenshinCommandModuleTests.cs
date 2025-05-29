#region

using System.Net;
using System.Text;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Common;
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
public class GenshinCommandModuleTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";
    private const string TestCharacterName = "Traveler";
    private const string TestGameUid = "800000001";
    private const string GoldenImagePath = "TestData/Genshin/Assets/GoldenImage.jpg";

    private ApplicationCommandService<ApplicationCommandContext> m_CommandService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ICharacterCardService<GenshinCharacterInformation>> m_CharacterCardServiceMock = null!;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private CommandRateLimitService m_CommandRateLimitService = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private GenshinCharacterCommandExecutor m_GenshinCharacterCommandExecutor = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private MongoTestHelper m_MongoTestHelper = null!;
    private ServiceProvider m_ServiceProvider = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;

    [SetUp]
    public async Task Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MongoTestHelper = new MongoTestHelper();

        // Set up command service
        var commandJson = new JsonApplicationCommand
        {
            Id = 123456789UL,
            Name = "genshin",
            Description = "Genshin Toolbox",
            Type = ApplicationCommandType.ChatInput
        };

        m_DiscordTestHelper = new DiscordTestHelper(commandJson);
        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        m_CommandService.AddModule<GenshinCommandModule>();
        await m_CommandService.CreateCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Create mocks for dependencies
        m_CharacterApiMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<GenshinCharacterInformation>>();
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
        SetupHttpClientMock();

        // Create real services with mocked dependencies
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        m_CommandRateLimitService = new CommandRateLimitService(
            m_DistributedCacheMock.Object,
            NullLogger<CommandRateLimitService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up default distributed cache behavior
        SetupDistributedCacheMock();

        // Set up character card service mock
        var imageBytes = await File.ReadAllBytesAsync(GoldenImagePath);
        m_CharacterCardServiceMock.Setup(s => s.GenerateCharacterCardAsync(
                It.IsAny<GenshinCharacterInformation>(),
                It.IsAny<string>()))
            .ReturnsAsync(() => new MemoryStream(imageBytes));

        // Create the executor with all dependencies
        m_GenshinCharacterCommandExecutor = new GenshinCharacterCommandExecutor(
            m_CharacterApiMock.Object,
            m_GameRecordApiService,
            m_CharacterCardServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_UserRepository,
            NullLogger<GenshinCharacterCommandExecutor>.Instance,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object
        );

        // Set up service provider
        m_ServiceProvider = new ServiceCollection()
            .AddSingleton(m_CommandService)
            .AddSingleton(m_UserRepository)
            .AddSingleton(m_CommandRateLimitService)
            .AddSingleton(m_TokenCacheService)
            .AddSingleton<ICharacterCommandService<GenshinCommandModule>>(m_GenshinCharacterCommandExecutor)
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
        m_ServiceProvider.Dispose();
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    private void SetupDistributedCacheMock()
    {
        // Default setup - no cache entries exist initially
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        m_DistributedCacheMock.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        m_DistributedCacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Test]
    public async Task CharacterCommand_WhenRateLimited_ReturnsRateLimitMessage()
    {
        // Arrange - Set up rate limit cache to simulate rate limit
        byte[] rateLimitData = "rate_limited"u8.ToArray();
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rateLimitData);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.ToLowerInvariant(), Contains.Substring("used command too frequent"));
    }

    [Test]
    public async Task CharacterCommand_WhenUserNotFound_ReturnsUserNotFoundMessage()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.ToLowerInvariant(), Contains.Substring("you do not have a profile"));
    }

    [Test]
    public async Task CharacterCommand_WhenNoTokenCached_TriggersAuthenticationModal()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Setup user with profile but no cached token
        var user = new UserModel
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
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return null (no cached token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(
            TestUserId, It.IsAny<IAuthenticationListener>()), Times.Once);

        var response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);
        Assert.That(response.ToLowerInvariant(), Contains.Substring("auth_modal"));
    }

    [Test]
    public async Task CharacterCommand_WhenAuthenticated_SendsCharacterCard()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
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
                    LtUid = TestLtUid,
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
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
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

        m_CharacterApiMock.Setup(s => s.GetAllCharactersAsync(
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

        m_CharacterApiMock.Setup(s => s.GetCharacterDataFromIdAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>()))
            .ReturnsAsync(characterResponse);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        m_CharacterApiMock.Verify(s => s.GetAllCharactersAsync(
            It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        m_CharacterApiMock.Verify(s => s.GetCharacterDataFromIdAsync(
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
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
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
                    LtUid = TestLtUid,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
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

        m_CharacterApiMock.Setup(s => s.GetAllCharactersAsync(
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

        m_CharacterApiMock.Setup(s => s.GetCharacterDataFromIdAsync(
                It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>()))
            .ReturnsAsync(characterResponse);

        // Act
        await ExecuteCharacterCommand(TestUserId, TestCharacterName, Regions.Asia);

        // Assert
        // Verify the user is updated with the new game UID
        var updatedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(updatedUser?.Profiles?.FirstOrDefault()?.GameUids, Is.Not.Null);
        Assert.That(updatedUser?.Profiles?.FirstOrDefault()?.GameUids, Contains.Key(GameName.Genshin));
        Assert.That(updatedUser?.Profiles?.FirstOrDefault()?.GameUids?[GameName.Genshin],
            Contains.Key(nameof(Regions.Asia)));
    }

    [Test]
    public async Task CharacterCommand_WhenCharacterNotFound_ReturnsNotFoundMessage()
    {
        // Arrange - Set up rate limit cache to simulate no rate limit
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
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
                    LtUid = TestLtUid,
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
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Setup character API to return empty list (character not found)
        m_CharacterApiMock.Setup(s => s.GetAllCharactersAsync(
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
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
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
                    LtUid = TestLtUid,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    }
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Setup distributed cache to return token
        byte[] tokenBytes = Encoding.UTF8.GetBytes(TestLToken);
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenBytes);

        // Force an exception in character API
        m_CharacterApiMock.Setup(s => s.GetAllCharactersAsync(
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

        // Create interaction with the correct subcommand name
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(
            userId,
            "character", // This is the subcommand name
            parameters.ToArray()
        );

        // Create context
        var context = new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Execute the command
        var result = await m_CommandService.ExecuteAsync(context, m_ServiceProvider);
        Assert.That(result, Is.Not.Null);
    }
}
