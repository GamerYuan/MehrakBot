#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Common;

[Parallelizable(ParallelScope.Self), FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class DailyCheckInServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;

    [SetUp]
    public void Setup()
    {
        m_DbFactory = new TestDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        m_DbFactory.Dispose();
    }

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_AlreadyCheckedInToday_ReturnsAlreadyCheckedInMessage()
    {
        // Arrange
        var (service, userContext, _, _) = SetupMocks();

        SeedTestUser(userContext, hasCheckedInToday: true);

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("already checked in today"));
        }
    }

    [Test]
    public async Task ExecuteAsync_NotCheckedInToday_ProceedsWithCheckIn()
    {
        // Arrange
        var (service, userContext, gameRecordApiMock, checkInApiMock) = SetupMocks();

        SeedTestUser(userContext, hasCheckedInToday: false);

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = true,
                Nickname = "TestPlayer",
                Region = "os_asia",
                Level = 60,
                GameId = 1
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.Success));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Check-in successful"));
        });

        var profile = await userContext.UserProfiles.SingleAsync();
        Assert.That(profile.LastCheckIn.HasValue, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_NoUserProfile_ProceedsWithCheckIn()
    {
        // Arrange
        var (service, _, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.HonkaiStarRail,
                HasRole = true,
                Nickname = "TestPlayer",
                Region = "prod_official_asia",
                Level = 70,
                GameId = 2
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.Success));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Check-in successful"));
        }
    }

    [Test]
    public async Task ExecuteAsync_InvalidCredentials_ReturnsAuthError()
    {
        // Arrange
        var (service, _, gameRecordApiMock, _) = SetupMocks();

        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 12345ul, "invalid_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoGameRecords_ReturnsNoGameRecordsMessage()
    {
        // Arrange
        var (service, _, gameRecordApiMock, _) = SetupMocks();

        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success([]));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("No game records found"));
        }
    }

    [Test]
    public async Task ExecuteAsync_MultipleGames_ChecksInAllGames()
    {
        // Arrange
        var (service, userContext, gameRecordApiMock, checkInApiMock) = SetupMocks();

        SeedTestUser(userContext, hasCheckedInToday: false);

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = true,
                Nickname = "GenshinPlayer",
                Region = "os_asia",
                Level = 60,
                GameId = 1
            },
            new()
            {
                Game = Game.HonkaiStarRail,
                HasRole = true,
                Nickname = "HSRPlayer",
                Region = "prod_official_asia",
                Level = 70,
                GameId = 2
            },
            new()
            {
                Game = Game.ZenlessZoneZero,
                HasRole = true,
                Nickname = "ZZZPlayer",
                Region = "prod_gf_jp",
                Level = 50,
                GameId = 3
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.Success));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("Genshin Impact"));
            Assert.That(content, Does.Contain("Honkai: Star Rail"));
            Assert.That(content, Does.Contain("Zenless Zone Zero"));
        }

        checkInApiMock.Verify(x => x.GetAsync(It.IsAny<CheckInApiContext>()), Times.Exactly(3));
        Assert.That((await userContext.UserProfiles.SingleAsync()).LastCheckIn.HasValue, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_AlreadyCheckedInStatus_ShowsAlreadyCheckedInMessage()
    {
        // Arrange
        var (service, _, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = true,
                Nickname = "TestPlayer",
                Region = "os_asia",
                Level = 60,
                GameId = 1
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.AlreadyCheckedIn));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Already checked in today"));
        }
    }

    [Test]
    public async Task ExecuteAsync_NoValidProfile_ShowsNoValidAccountMessage()
    {
        // Arrange
        var (service, _, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = false,
                Nickname = "",
                Region = "",
                Level = 0,
                GameId = 1
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.NoValidProfile));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("No valid account found"));
        });
    }

    [Test]
    public async Task ExecuteAsync_CheckInApiFails_ShowsErrorMessage()
    {
        // Arrange
        var (service, _, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = true,
                Nickname = "TestPlayer",
                Region = "os_asia",
                Level = 60,
                GameId = 1
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Failure(StatusCode.ExternalServerError, "API temporarily unavailable"));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("API temporarily unavailable"));
        });
    }

    [Test]
    public async Task ExecuteAsync_MixedResults_UpdatesUserOnlyIfAllSuccessful()
    {
        // Arrange
        var (service, userContext, gameRecordApiMock, checkInApiMock) = SetupMocks();

        SeedTestUser(userContext, hasCheckedInToday: false);

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = true,
                Nickname = "GenshinPlayer",
                Region = "os_asia",
                Level = 60,
                GameId = 1
            },
            new()
            {
                Game = Game.HonkaiStarRail,
                HasRole = true,
                Nickname = "HSRPlayer",
                Region = "prod_official_asia",
                Level = 70,
                GameId = 2
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.SetupSequence(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.Success))
            .ReturnsAsync(Result<CheckInStatus>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("Check-in successful"));
            Assert.That(content, Does.Contain("API Error"));
        });

        Assert.That((await userContext.UserProfiles.SingleAsync()).LastCheckIn.HasValue, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_CheckedInYesterdayUtc8_AllowsCheckInToday()
    {
        // Arrange
        var (service, userContext, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var profile = SeedTestUser(userContext, hasCheckedInToday: false);

        var cst = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cst);
        var yesterdayUtc8 = nowUtc8.AddDays(-1);
        var yesterdayUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, cst);
        profile.LastCheckIn = yesterdayUtc;
        await userContext.SaveChangesAsync();
        userContext.ChangeTracker.Clear();

        var gameRecords = new List<GameRecordDto>
        {
            new()
            {
                Game = Game.Genshin,
                HasRole = true,
                Nickname = "TestPlayer",
                Region = "os_asia",
                Level = 60,
                GameId = 1
            }
        };
        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success(gameRecords));

        checkInApiMock.Setup(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.Success));

        var context = CreateContext(1, 12345ul, "test_token");

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert - should proceed with check-in
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Check-in successful"));

        }

        Assert.That((await userContext.UserProfiles.SingleAsync()).LastCheckIn.HasValue, Is.True);
        Assert.That((await userContext.UserProfiles.SingleAsync()).LastCheckIn.Value, Is.GreaterThan(yesterdayUtc));
    }

    #endregion

    #region Helper Methods

    private (
        DailyCheckInService Service,
        UserDbContext UserContext,
        Mock<IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>> GameRecordApiMock,
        Mock<IApiService<CheckInStatus, CheckInApiContext>> CheckInApiMock
        ) SetupMocks()
    {
        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var gameRecordApiMock = new Mock<IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>>();
        var checkInApiMock = new Mock<IApiService<CheckInStatus, CheckInApiContext>>();
        var loggerMock = new Mock<ILogger<DailyCheckInService>>();

        var service = new DailyCheckInService(
            userContext,
            gameRecordApiMock.Object,
            checkInApiMock.Object,
            loggerMock.Object);

        return (service, userContext, gameRecordApiMock, checkInApiMock);
    }

    private static IApplicationContext CreateContext(ulong userId, ulong ltUid, string lToken)
    {
        var mock = new Mock<IApplicationContext>();
        mock.Setup(x => x.UserId).Returns(userId);
        mock.SetupGet(x => x.LtUid).Returns(ltUid);
        mock.SetupGet(x => x.LToken).Returns(lToken);
        return mock.Object;
    }

    private static UserProfileModel SeedTestUser(UserDbContext userContext, bool hasCheckedInToday)
    {
        var cst = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cst);

        DateTime? lastCheckIn = null;
        if (hasCheckedInToday)
        {
            var todayUtc8 = nowUtc8.Date.AddHours(10);
            lastCheckIn = TimeZoneInfo.ConvertTimeToUtc(todayUtc8, cst);
        }

        var user = new UserModel
        {
            Id = 1,
            Timestamp = DateTime.UtcNow
        };

        var profile = new UserProfileModel
        {
            User = user,
            UserId = user.Id,
            ProfileId = 1,
            LtUid = 12345L,
            LToken = "test_token",
            LastCheckIn = lastCheckIn
        };

        user.Profiles.Add(profile);
        userContext.Users.Add(user);
        userContext.SaveChanges();
        userContext.ChangeTracker.Clear();

        return profile;
    }

    #endregion
}
