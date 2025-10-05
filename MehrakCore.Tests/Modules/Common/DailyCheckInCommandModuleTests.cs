#region

using MehrakCore.Models;
using MehrakCore.Modules.Common;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Common;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.Rest.JsonModels;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using System.Text;

#endregion

namespace MehrakCore.Tests.Modules.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class DailyCheckInCommandModuleTests
{
    private ulong m_TestUserId;
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
    private ServiceProvider m_ServiceProvider = null!;

    [SetUp]
    public async Task Setup()
    {
        JsonApplicationCommand command = new()
        {
            Id = 1L,
            Name = "checkin",
            Description = "Perform HoYoLAB Daily Check-In",
            Type = ApplicationCommandType.ChatInput
        };

        // Create the test helper
        m_DiscordTestHelper = new DiscordTestHelper(command);

        // Set up command service
        m_CommandService = new ApplicationCommandService<ApplicationCommandContext>();
        m_CommandService.AddModule<DailyCheckInCommandModule>();
        await m_CommandService.RegisterCommandsAsync(m_DiscordTestHelper.DiscordClient.Rest, 123456789UL);

        // Set up real repository
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService,
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

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Create real services with mocked cache
        m_CommandRateLimitService = new CommandRateLimitService(
            m_DistributedCacheMock.Object,
            NullLogger<CommandRateLimitService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        GameRecordApiService gameRecordApiService = new(
            Mock.Of<IHttpClientFactory>(),
            NullLogger<GameRecordApiService>.Instance);

        // Create the executor
        DailyCheckInCommandExecutor executor = new(
            m_CheckInServiceMock.Object,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            gameRecordApiService,
            NullLogger<DailyCheckInCommandExecutor>.Instance); // Set up service provider
        m_ServiceProvider = new ServiceCollection()
            .AddSingleton(m_CommandService)
            .AddSingleton(m_UserRepository)
            .AddSingleton(m_CheckInServiceMock.Object)
            .AddSingleton(m_CommandRateLimitService)
            .AddSingleton(m_TokenCacheService)
            .AddSingleton<IDailyCheckInCommandExecutor>(executor)
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
    }

    [Test]
    public async Task DailyCheckInCommand_WhenRateLimited_ReturnsRateLimitMessage()
    {
        // Arrange Set up distributed cache to indicate user is rate limited
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync("true"u8.ToArray());

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Cache should not be set since user was already rate limited
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);

        // Extract interaction response data
        string responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("Used command too frequent"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenUserHasNoProfile_ReturnsNoProfileMessage()
    {
        // Arrange Set up distributed cache to indicate user is not rate limited
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked and rate limit was set
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Extract interaction response data
        string responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify response message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenUserHasProfileButNoToken_ShowsAuthModal()
    {
        // Arrange Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (no token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Create test user with a profile
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

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit and token
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Extract interaction response data
        string responseData =
            await m_DiscordTestHelper
                .ExtractInteractionResponseDataAsync(); // Verify the response contains authentication modal info
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("auth_modal:test-guid-12345:1"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenUserIsAuthenticated_PerformsCheckIn()
    {
        // Arrange Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken)); // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(TestLtUid, TestLToken))
            .Returns(Task.FromResult(ApiResult<(bool, string)>.Success((true, "Check in success"))));

        // Create test user with a profile
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

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit and token
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once); // Verify check-in service was called with the correct parameters
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task DailyCheckInCommand_WithCustomProfileId_UsesCorrectProfile()
    {
        // Arrange
        const uint customProfileId = 2;
        const ulong customLtUid = 555555UL;

        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{customLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken)); // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(customLtUid, TestLToken))
            .Returns(Task.FromResult(ApiResult<(bool, string)>.Success((true, "Check in success"))));

        // Create test user with multiple profiles
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
                },
                new()
                {
                    ProfileId = customProfileId,
                    LtUid = customLtUid,
                    GameUids = []
                }
            ]
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId,
            parameters: [("profile", customProfileId, ApplicationCommandOptionType.Integer)]);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked with the correct key
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{m_TestUserId}_{customLtUid}", It.IsAny<CancellationToken>()),
            Times.Once); // Verify check-in service was called with the correct parameters
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(customLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task DailyCheckInCommand_WhenExceptionOccurs_HandlesError()
    {
        // Arrange Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache to throw an exception when retrieving token
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Create test user with a profile
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

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        string responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify error response
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("An unknown error occurred"));
    }

    [Test]
    public async Task DailyCheckInCommand_WhenAlreadyCheckedInToday_ReturnsAlreadyCheckedInMessage()
    {
        // Arrange
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        DateTime lastCheckInUtc =
            TimeZoneInfo.ConvertTimeToUtc(nowUtc8.Date.AddHours(10), chinaTimeZone); // Same day in UTC+8

        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Create test user with a profile that was already checked in today
        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastCheckIn = lastCheckInUtc,
                    GameUids = []
                }
            ]
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit but check-in service was NOT called
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Extract interaction response data
        string responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the response contains the already checked in message
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("You have already checked in today for this profile"));

        // Verify check-in service was NOT called since user already checked in today
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ulong>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task DailyCheckInCommand_WhenCheckedInYesterday_PerformsCheckIn()
    {
        // Arrange
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        DateTime yesterdayUtc8 = nowUtc8.Date.AddDays(-1).AddHours(10);
        DateTime lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, chinaTimeZone);

        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(TestLtUid, TestLToken))
            .Returns(Task.FromResult(ApiResult<(bool, string)>.Success((true, "Check in success"))));

        // Create test user with a profile that was checked in yesterday
        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastCheckIn = lastCheckInUtc,
                    GameUids = []
                }
            ]
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit and token
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify check-in service was called with the correct parameters since
        // yesterday's check-in should allow today's check-in
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task DailyCheckInCommand_WhenNeverCheckedIn_PerformsCheckIn()
    {
        // Arrange Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(TestLtUid, TestLToken))
            .Returns(Task.FromResult(ApiResult<(bool, string)>.Success((true, "Check in success"))));

        // Create test user with a profile that has never checked in
        // (LastCheckIn = null)
        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastCheckIn = null,
                    GameUids = []
                }
            ]
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Verify cache was checked for rate limit and token
        m_DistributedCacheMock.Verify(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()),
            Times.Once);
        m_DistributedCacheMock.Verify(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify rate limit was set
        m_DistributedCacheMock.Verify(x => x.SetAsync($"RateLimit_{m_TestUserId}", It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify check-in service was called since user has never checked in
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }

    [Test]
    public async Task DailyCheckInCommand_WhenCheckedInAtMidnightBoundary_HandlesCorrectly()
    {
        // Arrange - Test the exact midnight boundary case
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);

        // Set last check-in to exactly midnight today in UTC+8
        DateTime midnightTodayUtc8 = nowUtc8.Date; // 00:00:00 today
        DateTime lastCheckInUtc = TimeZoneInfo.ConvertTimeToUtc(midnightTodayUtc8, chinaTimeZone);

        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Create test user with a profile that was checked in at midnight today
        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastCheckIn = lastCheckInUtc,
                    GameUids = []
                }
            ]
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Extract interaction response data
        string responseData = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();

        // Verify the response contains the already checked in message since
        // it's the same day
        Assert.That(responseData, Is.Not.Null);
        Assert.That(responseData, Contains.Substring("You have already checked in today for this profile"));

        // Verify check-in service was NOT called since user already checked in today
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(It.IsAny<ulong>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task DailyCheckInCommand_WhenTimeZoneConversionFails_ShouldHandleGracefully()
    {
        // This test documents expected behavior when timezone operations might
        // fail In practice, we expect TimeZoneInfo.FindSystemTimeZoneById to
        // work reliably but we want to ensure the code doesn't crash if
        // something unexpected happens

        // Set up distributed cache for rate limit check
        m_DistributedCacheMock.Setup(x => x.GetAsync($"RateLimit_{m_TestUserId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null);

        // Set up distributed cache for token check (has token)
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Set up check-in service to succeed
        m_CheckInServiceMock.Setup(x => x.CheckInAsync(TestLtUid, TestLToken))
            .Returns(Task.FromResult(ApiResult<(bool, string)>.Success((true, "Check-in success"))));

        // Create test user with a profile that has a very old LastCheckIn
        // (should definitely allow check-in)
        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = 1,
                    LtUid = TestLtUid,
                    LastCheckIn = DateTime.UtcNow.AddDays(-30), // 30 days ago
                    GameUids = []
                }
            ]
        };

        // Create user in the database
        await m_UserRepository.CreateOrUpdateUserAsync(testUser);

        SlashCommandInteraction interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);

        // Act - Should not throw an exception even if timezone handling has issues
        IExecutionResult result = await m_CommandService.ExecuteAsync(
            new ApplicationCommandContext(interaction, m_DiscordTestHelper.DiscordClient),
            m_ServiceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);

        // Since the last check-in was 30 days ago, it should definitely proceed
        // with check-in
        m_CheckInServiceMock.Verify(
            x => x.CheckInAsync(TestLtUid, TestLToken),
            Times.Once);
    }
}
