#region

using System.Text;
using MehrakCore.Models;
using MehrakCore.Modules.Common;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Common;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.Rest.JsonModels;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Tests.Modules.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class DailyCheckInCommandModuleTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";

    private ApplicationCommandService<ApplicationCommandContext> m_CommandService = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<IDailyCheckInService> m_CheckInServiceMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private CommandRateLimitService m_CommandRateLimitService = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private MongoTestHelper m_MongoTestHelper = null!;
    private ServiceProvider m_ServiceProvider = null!;

    [SetUp]
    public async Task Setup()
    {
        var command = new JsonApplicationCommand
        {
            Id = 1L,
            Name = "checkin",
            Description = "Perform HoYoLAB Daily Check-In",
            Type = ApplicationCommandType.ChatInput
        };

        // Create the test helper
        m_DiscordTestHelper = new DiscordTestHelper(command);

        // Set up MongoDB
        m_MongoTestHelper = new MongoTestHelper();

        // Set up command service
        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        m_CommandService.AddModule<DailyCheckInCommandModule>();
        await m_CommandService.CreateCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Set up real repository
        m_UserRepository =
            new UserRepository(m_MongoTestHelper.MongoDbService,
                NullLogger<UserRepository>.Instance); // Set up mocks for dependencies
        m_CheckInServiceMock = new Mock<IDailyCheckInService>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Set up authentication middleware to return TestGuid
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        // Set up default cache behavior for rate limiting (no rate limit by default)
        SetupDistributedCacheMock();

        // Create real services with mocked cache
        m_CommandRateLimitService = new CommandRateLimitService(
            m_DistributedCacheMock.Object,
            NullLogger<CommandRateLimitService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        // Create the executor
        var executor = new DailyCheckInCommandExecutor(
            m_CheckInServiceMock.Object,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            NullLogger<DailyCheckInCommandExecutor>.Instance); // Set up service provider
        m_ServiceProvider = new ServiceCollection()
            .AddSingleton(m_CommandService)
            .AddSingleton(m_UserRepository)
            .AddSingleton(m_CheckInServiceMock.Object)
            .AddSingleton(m_CommandRateLimitService)
            .AddSingleton(m_TokenCacheService)
            .AddSingleton<IDailyCheckInCommandService>(executor)
            .AddLogging(l => l.AddProvider(NullLoggerProvider.Instance))
            .BuildServiceProvider();
    }

    private void SetupDistributedCacheMock()
    {
        // Setup rate limit cache - default to no rate limit (null return)
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("RateLimit_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Setup token cache - default to no token (null return)
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    [TearDown]
    public void TearDown()
    {
        m_ServiceProvider.Dispose();
        m_DiscordTestHelper.Dispose();
        m_MongoTestHelper.Dispose();
    }

    [Test]
    public async Task DailyCheckInCommand_WhenRateLimited_ReturnsRateLimitMessage()
    {
        // Arrange
        // Set up distributed cache to indicate user is rate limited
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true"u8.ToArray());

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Cache should not be set since user was already rate limited
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("Used command too frequent"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenUserHasNoProfile_ReturnsNoProfileMessage()
    {
        // Arrange
        // Set up distributed cache to indicate user is not rate limited
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked and rate limit was set
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenUserHasProfileButNoToken_ShowsAuthModal()
    {
        // Arrange
        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (no token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Create test user with a profile
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

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit and token
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Extract interaction response data
        var responseData =
            await m_DiscordTestHelper
                .ExtractInteractionResponseDataAsync(); // Verify the response contains authentication modal info
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("auth_modal:test-guid-12345:1"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenUserIsAuthenticated_PerformsCheckIn()
    {
        // Arrange
        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), TestLtUid, TestLToken))
            .Returns(Task.CompletedTask);

        // Create test user with a profile
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

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit and token
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify check-in service was called with the correct parameters
        m_CheckInServiceMock.Verify(x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task DailyCheckInCommand_WithCustomProfileId_UsesCorrectProfile()
    {
        // Arrange
        const uint customProfileId = 2;
        const ulong customLtUid = 555555UL;

        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{customLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), customLtUid, TestLToken))
            .Returns(Task.CompletedTask);

        // Create test user with multiple profiles
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
                },
                new()
                {
                    ProfileId = customProfileId,
                    LtUid = customLtUid,
                    GameUids = new Dictionary<GameName, Dictionary<string, string>>()
                }
            }
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId,
            parameters: [("profile", customProfileId, ApplicationCommandOptionType.Integer)]);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked with the correct key
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{customLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify check-in service was called with the correct parameters
        m_CheckInServiceMock.Verify(x => x.CheckInAsync(It.IsAny<ApplicationCommandContext>(), customLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task DailyCheckInCommand_WhenExceptionOccurs_HandlesError()
    {
        // Arrange
        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache to throw an exception when retrieving token
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Create test user with a profile
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

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        var interaction = m_DiscordTestHelper.CreateCommandInteraction(TestUserId);

        // Act
        var result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        var responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify error response
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("An error occurred"));
    }
}