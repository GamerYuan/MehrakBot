#region

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
using NetCord;
using NetCord.JsonModels;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Tests.Modules;

[Parallelizable(ParallelScope.Fixtures)]
public class AuthModalModuleTests
{
    private DiscordTestHelper m_DiscordTestHelper;
    private MongoTestHelper m_MongoTestHelper;
    private UserRepository m_UserRepository;
    private ServiceProvider m_ServiceProvider;
    private Mock<IDistributedCache> m_DistributedCacheMock;
    private TokenCacheService m_TokenCacheService;
    private CookieService m_CookieService;
    private GenshinCharacterCommandService<ModalInteractionContext> m_CommandService;
    private ComponentInteractionService<ModalInteractionContext> m_Service;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>> m_CharacterApiServiceMock;
    private GameRecordApiService m_GameRecordApiService;
    private Mock<GenshinImageUpdaterService> m_ImageUpdaterServiceMock;
    private Mock<ICharacterCardService<GenshinCharacterInformation>> m_CharacterCardServiceMock;

    private string m_EncryptedToken;

    private const string SamplePassphrase = "sample_passphrase";
    private const string SampleToken = "sample_token";
    private const ulong TestUserId = 123456789UL;

    [SetUp]
    public void Setup()
    {
        m_DiscordTestHelper = new DiscordTestHelper();
        m_MongoTestHelper = new MongoTestHelper();
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);
        m_Service = new ComponentInteractionService<ModalInteractionContext>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_CookieService = new CookieService(NullLogger<CookieService>.Instance);
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_CharacterApiServiceMock = new Mock<ICharacterApi<GenshinBasicCharacterData, GenshinCharacterDetail>>();
        m_GameRecordApiService = new GameRecordApiService(m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<GenshinCharacterInformation>>(MockBehavior.Loose);
        m_ImageUpdaterServiceMock = new Mock<GenshinImageUpdaterService>(MockBehavior.Loose,
            new Mock<ImageRepository>(m_MongoTestHelper.MongoDbService, NullLogger<ImageRepository>.Instance).Object,
            m_HttpClientFactoryMock.Object,
            NullLogger<GenshinImageUpdaterService>.Instance);

        m_CommandService = new GenshinCharacterCommandService<ModalInteractionContext>(
            m_CharacterApiServiceMock.Object,
            m_GameRecordApiService,
            m_CharacterCardServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_UserRepository,
            NullLogger<GenshinCharacterCommandService<ModalInteractionContext>>.Instance);

        m_Service.AddModule<AuthModalModule>();

        m_EncryptedToken = m_CookieService.EncryptCookie(SampleToken, SamplePassphrase);

        m_ServiceProvider = new ServiceCollection()
            .AddSingleton(m_UserRepository)
            .AddSingleton(m_Service)
            .AddSingleton(m_TokenCacheService)
            .AddSingleton(m_CookieService)
            .AddSingleton(m_CommandService)
            .AddLogging(l => l.AddProvider(NullLoggerProvider.Instance))
            .BuildServiceProvider();

        SetupDistributedCacheMock();
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
        m_ServiceProvider.Dispose();
    }

    private void SetupDistributedCacheMock()
    {
        // Default setup for GetAsync to simulate a cache miss
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Default setup for SetAsync
        m_DistributedCacheMock.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Default setup for RemoveAsync
        m_DistributedCacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static JsonUser CreateTestUser(ulong userId = TestUserId)
    {
        return new JsonUser
        {
            Id = userId,
            Username = "TestUser",
            Discriminator = 0000,
            IsBot = false
        };
    }

    private JsonInteraction CreateBaseInteraction(JsonUser user)
    {
        return new JsonInteraction
        {
            ApplicationId = 123456789UL,
            Token = "sample_token",
            User = user,
            Entitlements = [],
            Type = InteractionType.Modal,
            Channel = new JsonChannel
            {
                Id = 123456789UL,
                Type = ChannelType.TextGuildChannel
            },
            GuildId = 123456789UL,
            GuildUser = new JsonGuildUser
            {
                Deafened = false,
                Muted = false,
                User = user
            },
            Message = new JsonMessage
            {
                Author = user,
                ChannelId = 123456789UL,
                GuildId = 123456789UL,
                Id = 123456789UL,
                MentionedUsers = [],
                Attachments = [],
                Embeds = []
            }
        };
    }

    [Test]
    public async Task CharacterAuth_ValidPassphrase_Success()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "character_auth_modal:Traveler:Asia:1",
            Components =
            [
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "passphrase",
                            Value = SamplePassphrase
                        }
                    ]
                }
            ]
        };

        var userModel = new UserModel
        {
            Id = TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LastUsedRegions = new Dictionary<GameName, Regions>
                    {
                        { GameName.Genshin, Regions.Asia }
                    },
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>
                    {
                        { GameName.Genshin, new Dictionary<string, string> { { nameof(Regions.Asia), "800000001" } } }
                    },
                    LtUid = 123456789UL,
                    LToken = m_EncryptedToken
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Is.Not.Null);
        });
    }

    [Test]
    public async Task CharacterAuth_UserNotFound_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "character_auth_modal:Traveler:Asia:1",
            Components =
            [
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "passphrase",
                            Value = SamplePassphrase
                        }
                    ]
                }
            ]
        };

        // No user in database

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Contains.Substring("No profile found!"));
        });
    }

    [Test]
    public async Task CharacterAuth_WrongPassphrase_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "character_auth_modal:Traveler:Asia:1",
            Components =
            [
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "passphrase",
                            Value = "wrong_passphrase" // Wrong passphrase
                        }
                    ]
                }
            ]
        };

        var userModel = new UserModel
        {
            Id = TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 123456789UL,
                    LToken = m_EncryptedToken
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Contains.Substring("Invalid passphrase"));
        });
    }

    [Test]
    public async Task AddAuth_ValidInput_Success()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "add_auth_modal",
            Components =
            [
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "ltuid",
                            Value = "987654321"
                        }
                    ]
                },
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "ltoken",
                            Value = "new_token_value"
                        }
                    ]
                },
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "passphrase",
                            Value = "new_passphrase"
                        }
                    ]
                }
            ]
        };

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.That(result, Is.Not.Null);

        // Check that the user was created with the correct profile
        var savedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(savedUser, Is.Not.Null);
        Assert.That(savedUser.Profiles?.ToList(), Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(savedUser.Profiles.First().LtUid, Is.EqualTo(987654321UL));
            Assert.That(extract, Contains.Substring("Added profile successfully!"));
        });
    }

    [Test]
    public async Task AddAuth_InvalidUidFormat_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "add_auth_modal",
            Components =
            [
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "ltuid",
                            Value = "not-a-number" // Invalid UID
                        }
                    ]
                },
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "ltoken",
                            Value = "token_value"
                        }
                    ]
                },
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "passphrase",
                            Value = "passphrase_value"
                        }
                    ]
                }
            ]
        };

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Contains.Substring("Invalid UID!"));
        });
    }

    [Test]
    public async Task AddAuth_ProfileAlreadyExists_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "add_auth_modal",
            Components =
            [
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "ltuid",
                            Value = "123456789" // Same UID as existing profile
                        }
                    ]
                },
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "ltoken",
                            Value = "token_value"
                        }
                    ]
                },
                new JsonComponent
                {
                    Type = ComponentType.ActionRow,
                    Components =
                    [
                        new JsonComponent
                        {
                            Type = ComponentType.TextInput,
                            CustomId = "passphrase",
                            Value = "passphrase_value"
                        }
                    ]
                }
            ]
        };

        // Create user with existing profile
        var userModel = new UserModel
        {
            Id = TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 123456789UL, // Same UID as input
                    LToken = m_EncryptedToken
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Contains.Substring("Profile already exists!"));
        });
    }

    [Test]
    public async Task AddAuth_Exception_HandlesError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser();
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "add_auth_modal",
            Components = [] // Missing required components to trigger exception
        };

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Contains.Substring("An error occurred"));
        });
    }
}
