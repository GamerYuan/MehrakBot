#region

using MehrakCore.Models;
using MehrakCore.Modules.Common;
using MehrakCore.Repositories;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetCord;
using NetCord.Rest.JsonModels;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Tests.Modules.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class ProfileCommandModuleTests
{
    private const ulong TestUserId = 123456789UL;

    private ApplicationCommandService<ApplicationCommandContext> m_CommandService;
    private UserRepository m_UserRepository;
    private DiscordTestHelper m_DiscordTestHelper;
    private MongoTestHelper m_MongoTestHelper;
    private ServiceProvider m_ServiceProvider;

    [SetUp]
    public async Task Setup()
    {
        var command = new JsonApplicationCommand
        {
            Id = 1L,
            Name = "profile",
            Description = "Manage your profile",
            Type = ApplicationCommandType.ChatInput
        };

        // Create the test helper
        m_DiscordTestHelper = new DiscordTestHelper(command);

        // Set up MongoDB
        m_MongoTestHelper = new MongoTestHelper();

        // Set up command service
        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        m_CommandService.AddModule<ProfileCommandModule>();
        await m_CommandService.CreateCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Set up real repository
        m_UserRepository = new UserRepository(m_MongoTestHelper.MongoDbService, NullLogger<UserRepository>.Instance);

        // Set up service provider
        m_ServiceProvider = new ServiceCollection().AddSingleton(m_CommandService).AddSingleton(m_UserRepository)
            .AddLogging(l => l.AddProvider(NullLoggerProvider.Instance))
            .BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        m_ServiceProvider.Dispose();
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    [Test]
    public async Task ListProfileCommand_WithProfiles_DisplaysCorrectProfiles()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, LtUid = 111111, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                },
                new()
                {
                    ProfileId = 2, LtUid = 222222, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_DiscordTestHelper.ClearCapturedRequests();

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "list");

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Find interaction response request
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the response contains both profile IDs
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("Profile 1"));
        Assert.That(responseData, Contains.Substring("111111"));
        Assert.That(responseData, Contains.Substring("Profile 2"));
        Assert.That(responseData, Contains.Substring("222222"));
    }

    [Test]
    public async Task ListProfileCommand_WithNoProfiles_DisplaysNoProfilesMessage()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>()
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_DiscordTestHelper.ClearCapturedRequests();

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "list");

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("No profile found"));
    }

    [Test]
    public async Task DeleteProfileCommand_WithSpecificId_RemovesCorrectProfile()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, LtUid = 111111, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                },
                new()
                {
                    ProfileId = 2, LtUid = 222222, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_DiscordTestHelper.ClearCapturedRequests();

        // Set up delete command with profile ID 1
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "delete",
            ("profile", 1, ApplicationCommandOptionType.Integer));

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify the profile with ID 1 was removed
        var updatedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(updatedUser, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(updatedUser.Profiles!.Count(), Is.EqualTo(1));
            Assert.That(updatedUser.Profiles!.First().ProfileId,
                Is.EqualTo(1u)); // The second profile should now have ID 1
            Assert.That(updatedUser.Profiles!.First().LtUid, Is.EqualTo(222222UL));
        });

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("Profile 1 deleted"));
    }

    [Test]
    public async Task DeleteProfileCommand_WithNoId_RemovesAllProfiles()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1, LtUid = 111111, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                },
                new()
                {
                    ProfileId = 2, LtUid = 222222, GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_DiscordTestHelper.ClearCapturedRequests();

        // Set up delete command with no profile ID (delete all)
        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "delete");

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify user was deleted
        var deletedUser = await m_UserRepository.GetUserAsync(TestUserId);
        Assert.That(deletedUser, Is.Null);

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("All profiles deleted"));
    }

    [Test]
    public async Task DeleteProfileCommand_WithNoProfiles_ShowsNoProfileMessage()
    {
        // Arrange
        var testUser = new UserModel
        {
            Id = TestUserId,
            Profiles = new List<UserProfile>()
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        // Clear captured requests
        m_DiscordTestHelper.ClearCapturedRequests();

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId, "delete",
            ("profile", 1, ApplicationCommandOptionType.Integer));

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("No profile with ID 1 found"));
    }
}