#region

using MehrakCore.Models;
using MehrakCore.Modules.Common;
using Mehrak.Infrastructure.Services;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using Mehrak.Bot.Modules.Common;
using Mehrak.Domain.Interfaces;

#endregion

namespace MehrakCore.Tests.Modules.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class AuthModalModuleTests
{
    private DiscordTestHelper m_DiscordTestHelper;
    private UserRepository m_UserRepository;
    private ServiceProvider m_ServiceProvider;
    private Mock<IDistributedCache> m_DistributedCacheMock;
    private TokenCacheService m_TokenCacheService;
    private CookieService m_CookieService;
    private ComponentInteractionService<ModalInteractionContext> m_Service;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock;

    private string m_EncryptedToken;

    private const string SamplePassphrase = "sample_passphrase";
    private const string SampleToken = "sample_token";
    private ulong m_TestUserId;

    [SetUp]
    public void Setup()
    {
        m_DiscordTestHelper = new DiscordTestHelper();
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);
        m_Service = new ComponentInteractionService<ModalInteractionContext>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_TokenCacheService =
            new TokenCacheService(m_DistributedCacheMock.Object, NullLogger<TokenCacheService>.Instance);
        m_CookieService = new CookieService(NullLogger<CookieService>.Instance);
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        m_Service.AddModule<AuthModalModule>();

        m_EncryptedToken = m_CookieService.EncryptCookie(SampleToken, SamplePassphrase);

        m_ServiceProvider = new ServiceCollection()
            .AddSingleton(m_UserRepository)
            .AddSingleton(m_Service)
            .AddSingleton(m_TokenCacheService)
            .AddSingleton(m_CookieService)
            .AddSingleton(m_AuthenticationMiddlewareMock.Object)
            .AddLogging(l => l.AddProvider(NullLoggerProvider.Instance))
            .BuildServiceProvider();

        SetupDistributedCacheMock();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();
    }

    [TearDown]
    public void TearDown()
    {
        m_DiscordTestHelper.Dispose();
        m_ServiceProvider.Dispose();
    }

    private void SetupDistributedCacheMock()
    {
        // Default setup for GetAsync to simulate a cache miss
        m_DistributedCacheMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

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

    private JsonUser CreateTestUser(ulong userId)
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
    public async Task AuthModal_ValidPassphrase_Success()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser(m_TestUserId);
        var jsonInteraction = CreateBaseInteraction(user);
        var testGuid = "test-guid-12345";
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = $"auth_modal:{testGuid}:1",
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
            Id = m_TestUserId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LastUsedRegions = new Dictionary<Game, Regions>
                    {
                        { Game.Genshin, Regions.Asia }
                    },
                    GameUids = new Dictionary<Game, Dictionary<string, string>>
                    {
                        { Game.Genshin, new Dictionary<string, string> { { nameof(Regions.Asia), "800000001" } } }
                    },
                    LtUid = 123456789UL,
                    LToken = m_EncryptedToken
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(userModel);

        m_AuthenticationMiddlewareMock.Setup(x => x.ContainsAuthenticationRequest(It.IsAny<string>())).Returns(true);

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            // Verify that the authentication middleware was notified
            m_AuthenticationMiddlewareMock.Verify(x => x.NotifyAuthenticationCompletedAsync(
                testGuid, It.Is<AuthenticationResult>(r => r.IsSuccess)), Times.Once);
        });
    }

    [Test]
    public async Task AuthModal_UserNotFound_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser(m_TestUserId);
        var jsonInteraction = CreateBaseInteraction(user);
        var testGuid = "test-guid-12345";
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = $"auth_modal:{testGuid}:1",
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

        m_AuthenticationMiddlewareMock.Setup(x => x.ContainsAuthenticationRequest(It.IsAny<string>())).Returns(true);

        // No user in database

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            // Verify that the authentication middleware was notified with failure
            m_AuthenticationMiddlewareMock.Verify(x => x.NotifyAuthenticationCompletedAsync(
                    testGuid, It.Is<AuthenticationResult>(r => !r.IsSuccess && r.ErrorMessage == "No profile found")),
                Times.Once);
        });
    }

    [Test]
    public async Task AuthModal_WrongPassphrase_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser(m_TestUserId);
        var jsonInteraction = CreateBaseInteraction(user);
        var testGuid = "test-guid-12345";
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = $"auth_modal:{testGuid}:1",
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
            Id = m_TestUserId,
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

        m_AuthenticationMiddlewareMock.Setup(x => x.ContainsAuthenticationRequest(It.IsAny<string>())).Returns(true);

        await m_UserRepository.CreateOrUpdateUserAsync(userModel);

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient); // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            // Verify that the authentication middleware was notified with failure
            m_AuthenticationMiddlewareMock.Verify(x => x.NotifyAuthenticationCompletedAsync(
                    testGuid, It.Is<AuthenticationResult>(r => !r.IsSuccess && r.ErrorMessage == "Invalid passphrase")),
                Times.Once);
        });
    }

    [Test]
    public async Task AuthModal_RequestTimedOut_ReturnsError()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser(m_TestUserId);
        var jsonInteraction = CreateBaseInteraction(user);
        var testGuid = "test-guid-12345";
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = $"auth_modal:{testGuid}:1",
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
            Id = m_TestUserId,
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

        m_AuthenticationMiddlewareMock.Setup(x => x.ContainsAuthenticationRequest(It.IsAny<string>())).Returns(false);

        await m_UserRepository.CreateOrUpdateUserAsync(userModel);

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(await m_DiscordTestHelper.ExtractInteractionResponseDataAsync(),
            Contains.Substring("This authentication request has expired or is invalid"));
    }

    [Test]
    public async Task AddAuth_ValidInput_Success()
    {
        // Arrange
        m_DiscordTestHelper.SetupRequestCapture();
        var user = CreateTestUser(m_TestUserId);
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
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient);

        // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.That(result, Is.Not.Null);

        // Check that the user was created with the correct profile
        var savedUser = await m_UserRepository.GetUserAsync(m_TestUserId);
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
        var user = CreateTestUser(m_TestUserId);
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
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
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
        var user = CreateTestUser(m_TestUserId);
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
            Id = m_TestUserId,
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
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
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
        var user = CreateTestUser(m_TestUserId);
        var jsonInteraction = CreateBaseInteraction(user);
        jsonInteraction.Data = new JsonInteractionData
        {
            CustomId = "add_auth_modal",
            Components = [] // Missing required components to trigger exception
        };

        var interaction = new ModalInteraction(jsonInteraction, null,
            async (interaction, callback, _, _, cancellationToken) =>
                await m_DiscordTestHelper.DiscordClient.Rest.SendInteractionResponseAsync(interaction.Id,
                    interaction.Token, callback,
                    false, null, cancellationToken),
            m_DiscordTestHelper.DiscordClient.Rest);
        var context = new ModalInteractionContext(interaction, m_DiscordTestHelper.DiscordClient); // Act
        var result = await m_Service.ExecuteAsync(context, m_ServiceProvider);
        var extract = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(extract, Contains.Substring("An error occurred"));
        });
    }

    [Test]
    public void AuthModal_CreatesCorrectModal()
    {
        // Arrange & Act
        var modal = AuthModalModule.AuthModal("test-guid-12345", 1);

        // Assert
        Assert.That(modal.CustomId, Is.EqualTo("auth_modal:test-guid-12345:1"));
        Assert.That(modal.Title, Is.EqualTo("Authenticate"));
        Assert.That(modal.Components.Count, Is.EqualTo(1));

        var actionRow = modal.Components.First();
        Assert.That(actionRow, Is.Not.Null);
        Assert.That(actionRow, Is.TypeOf<TextInputProperties>());

        var textInput = actionRow as TextInputProperties;
        Assert.Multiple(() =>
        {
            Assert.That(textInput!.CustomId, Is.EqualTo("passphrase"));
            Assert.That(textInput.Style, Is.EqualTo(TextInputStyle.Paragraph));
            Assert.That(textInput.Label, Is.EqualTo("Passphrase"));
            Assert.That(textInput.Placeholder, Is.EqualTo("Your Passphrase"));
            Assert.That(textInput.MaxLength, Is.EqualTo(64));
        });
    }

    [Test]
    public void AddAuthModal_CreatesCorrectModal()
    {
        // Arrange & Act
        var modal = AuthModalModule.AddAuthModal;

        // Assert
        Assert.That(modal.CustomId, Is.EqualTo("add_auth_modal"));
        Assert.That(modal.Title, Is.EqualTo("Authenticate"));
        Assert.That(modal.Components.Count, Is.EqualTo(3));

        var components = modal.Components.ToList();

        // Check ltuid component
        var ltuidInput = components[0] as TextInputProperties;
        Assert.That(ltuidInput, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(ltuidInput!.CustomId, Is.EqualTo("ltuid"));
            Assert.That(ltuidInput.Style, Is.EqualTo(TextInputStyle.Short));
            Assert.That(ltuidInput.Label, Is.EqualTo("HoYoLAB UID"));
        });

        // Check ltoken component
        var ltokenInput = components[1] as TextInputProperties;
        Assert.That(ltokenInput, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(ltokenInput!.CustomId, Is.EqualTo("ltoken"));
            Assert.That(ltokenInput.Style, Is.EqualTo(TextInputStyle.Paragraph));
            Assert.That(ltokenInput.Label, Is.EqualTo("HoYoLAB Cookies"));
        });

        // Check passphrase component
        var passphraseInput = components[2] as TextInputProperties;
        Assert.That(passphraseInput, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(passphraseInput!.CustomId, Is.EqualTo("passphrase"));
            Assert.That(passphraseInput.Style, Is.EqualTo(TextInputStyle.Paragraph));
            Assert.That(passphraseInput.Label, Is.EqualTo("Passphrase"));
            Assert.That(passphraseInput.MaxLength, Is.EqualTo(64));
            Assert.That(passphraseInput.Placeholder,
                Is.EqualTo("Do not use the same password as your Discord or HoYoLAB account!"));
        });
    }
}
