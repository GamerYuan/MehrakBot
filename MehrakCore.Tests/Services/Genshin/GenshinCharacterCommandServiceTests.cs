#region

using System.Net;
using System.Text;
using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Genshin;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Services;

#endregion

namespace MehrakCore.Tests.Services.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharacterCommandServiceTests
{
    private GenshinCharacterCommandService<IInteractionContext> m_Service;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiMock;
    private GameRecordApiService m_GameRecordApiService;
    private Mock<ICharacterCardService<GenshinCharacterInformation>> m_CharacterCardServiceMock;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock;
    private UserRepository m_UserRepository;
    private Mock<ILogger<GenshinCharacterCommandService<IInteractionContext>>> m_LoggerMock;
    private DiscordTestHelper m_DiscordTestHelper;
    private MongoTestHelper m_MongoTestHelper;
    private Mock<IInteractionContext> m_ContextMock;
    private SlashCommandInteraction m_Interaction;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock;

    [SetUp]
    public void Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MongoTestHelper = new MongoTestHelper();

        // Create mocks for dependencies
        m_CharacterApiMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<GenshinCharacterInformation>>();

        var imageRepository =
            new ImageRepository(m_MongoTestHelper.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(
            imageRepository,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<GenshinImageUpdaterService>>());
        m_LoggerMock = new Mock<ILogger<GenshinCharacterCommandService<IInteractionContext>>>();

        // Set up mocked HTTP handler and client factory
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Create real GameRecordApiService with mocked HTTP client
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up interaction context
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(123456789UL);
        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Set up Discord test helper to capture responses
        m_DiscordTestHelper.SetupRequestCapture();

        // Create the service under test
        m_Service = new GenshinCharacterCommandService<IInteractionContext>(
            m_CharacterApiMock.Object,
            m_GameRecordApiService,
            m_CharacterCardServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_UserRepository,
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
        m_MongoTestHelper.Dispose();
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenUserProfileDoesNotExist_ShouldSendErrorMessage()
    {
        // Assign
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No profile found"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenGameUidNotFoundAndApiReturnsError_ShouldSendErrorMessage()
    {
        // Assign
        const ulong userId = 123456789UL;
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string characterName = "Traveler";
        const Regions server = Regions.Asia;

        // Create and save user with profile but no game UID
        var user = new UserModel
        {
            Id = userId,
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

        // Mock HTTP handler to return error response
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { retcode = -100, message = "Invalid cookies" }));

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterNotFound_ShouldSendErrorMessage()
    {
        // Assign
        const ulong userId = 123456789UL;
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "NonExistentCharacter";
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = userId,
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
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
            .ReturnsAsync(new List<GenshinBasicCharacterData>());

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterDetailsFail_ShouldSendErrorMessage()
    {
        // Assign
        const ulong userId = 123456789UL;
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = userId,
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
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
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
        m_CharacterApiMock.Setup(x =>
                x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail> { RetCode = 10001 });

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Invalid HoYoLAB UID or Cookies"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenCharacterDetailsEmpty_ShouldSendErrorMessage()
    {
        // Assign
        const ulong userId = 123456789UL;
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = userId,
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
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
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
        m_CharacterApiMock.Setup(x =>
                x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
            .ReturnsAsync(new ApiResult<GenshinCharacterDetail>
            {
                RetCode = 0,
                Data = new GenshinCharacterDetail
                    { List = new List<GenshinCharacterInformation>(), AvatarWiki = new Dictionary<string, string>() }
            });

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Failed to retrieve character data"));
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenAllSuccessful_ShouldSendCharacterCard()
    {
        // Assign
        const ulong userId = 123456789UL;
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile and game UID
        var user = new UserModel
        {
            Id = userId,
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
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
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
        var characterInfo = new GenshinCharacterInformation
        {
            Base = new BaseCharacterDetail
            {
                Id = characterId,
                Name = characterName,
                Weapon = new Weapon
                {
                    Id = 11406,
                    Icon = "",
                    Name = null!
                },
                Icon = ""
            },
            Weapon = new WeaponDetail
            {
                Id = 11406,
                Name = "Test Weapon",
                Icon = "",
                TypeName = "",
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

        // Mock character detail API to return valid data
        m_CharacterApiMock.Setup(x =>
                x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
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
        m_CharacterCardServiceMock.Setup(x =>
                x.GenerateCharacterCardAsync(characterInfo, gameUid))
            .ReturnsAsync(new MemoryStream(new byte[100]));

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        // Verify the character card was generated
        m_CharacterCardServiceMock.Verify(x =>
            x.GenerateCharacterCardAsync(characterInfo, gameUid), Times.Once);

        // Verify the image updater was called
        m_ImageUpdaterServiceMock.Verify(x =>
            x.UpdateDataAsync(characterInfo, It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Once);

        // Verify response was sent with attachment
        var fileBytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(fileBytes, Is.Not.Null);
    }

    [Test]
    public async Task SendCharacterCardResponseAsync_WhenNoGameUidButApiSucceeds_ShouldUpdateProfileAndSendCard()
    {
        // Assign
        const ulong userId = 123456789UL;
        const ulong ltuid = 123456UL;
        const string ltoken = "valid_token";
        const string gameUid = "800800800";
        const string characterName = "Traveler";
        const int characterId = 10000007;
        const Regions server = Regions.Asia;

        // Create and save user with profile but no game UID
        var user = new UserModel
        {
            Id = userId,
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
        m_CharacterApiMock.Setup(x =>
                x.GetAllCharactersAsync(ltuid, ltoken, gameUid, "os_asia"))
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
        var characterInfo = new GenshinCharacterInformation
        {
            Base = new BaseCharacterDetail
            {
                Id = characterId,
                Name = characterName,
                Weapon = new Weapon
                {
                    Id = 11406,
                    Icon = "",
                    Name = ""
                },
                Icon = ""
            },
            Weapon = new WeaponDetail
            {
                Id = 11406,
                Name = "Test Weapon",
                Icon = "",
                TypeName = "",
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

        // Mock character detail API to return valid data
        m_CharacterApiMock.Setup(x =>
                x.GetCharacterDataFromIdAsync(ltuid, ltoken, gameUid, "os_asia", characterId))
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
        m_CharacterCardServiceMock.Setup(x =>
                x.GenerateCharacterCardAsync(characterInfo, gameUid))
            .ReturnsAsync(new MemoryStream(new byte[100]));

        // Act
        await m_Service.SendCharacterCardResponseAsync(ltuid, ltoken, characterName, server);

        // Assert
        // Verify the character card was generated
        m_CharacterCardServiceMock.Verify(x =>
            x.GenerateCharacterCardAsync(characterInfo, gameUid), Times.Once);

        // Verify user profile was updated with the game UID
        var updatedUser = await m_UserRepository.GetUserAsync(userId);
        Assert.That(updatedUser?.Profiles?.First().GameUids?[GameName.Genshin][server.ToString()], Is.EqualTo(gameUid));
        Assert.That(updatedUser?.Profiles?.First().LastUsedRegions?[GameName.Genshin], Is.EqualTo(server));

        // Verify response was sent with attachment
        var fileBytes = await m_DiscordTestHelper.ExtractInteractionResponseAsBytesAsync();
        Assert.That(fileBytes, Is.Not.Null);
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
}
