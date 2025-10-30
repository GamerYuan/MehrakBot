#region

using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Common;

[Parallelizable(ParallelScope.Self)]
public class DailyCheckInServiceTests
{
    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_AlreadyCheckedInToday_ReturnsAlreadyCheckedInMessage()
    {
        // Arrange
        var (service, userRepositoryMock, _, _) = SetupMocks();

        var user = CreateTestUser(hasCheckedInToday: true);
        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync(user);

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("already checked in today"));
        });

        // Verify no API calls were made
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_NotCheckedInToday_ProceedsWithCheckIn()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var user = CreateTestUser(hasCheckedInToday: false);
        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync(user);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Check-in successful"));
        });

        // Verify user was updated
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.Is<UserModel>(
            u => u.Profiles!.First().LastCheckIn.HasValue)), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoUserProfile_ProceedsWithCheckIn()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((UserModel?)null);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Check-in successful"));
        });
    }

    [Test]
    public async Task ExecuteAsync_InvalidCredentials_ReturnsAuthError()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, _) = SetupMocks();

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((UserModel?)null);

        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "invalid_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        });
    }

    [Test]
    public async Task ExecuteAsync_NoGameRecords_ReturnsNoGameRecordsMessage()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, _) = SetupMocks();

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((UserModel?)null);

        gameRecordApiMock.Setup(x => x.GetAsync(It.IsAny<GameRecordApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GameRecordDto>>.Success([]));

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("No game records found"));
        });
    }

    [Test]
    public async Task ExecuteAsync_MultipleGames_ChecksInAllGames()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var user = CreateTestUser(hasCheckedInToday: false);
        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync(user);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("Genshin Impact"));
            Assert.That(content, Does.Contain("Honkai: Star Rail"));
            Assert.That(content, Does.Contain("Zenless Zone Zero"));
        });

        // Verify check-in was called for each game
        checkInApiMock.Verify(x => x.GetAsync(It.IsAny<CheckInApiContext>()), Times.Exactly(3));
    }

    [Test]
    public async Task ExecuteAsync_AlreadyCheckedInStatus_ShowsAlreadyCheckedInMessage()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((UserModel?)null);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Already checked in today"));
        });
    }

    [Test]
    public async Task ExecuteAsync_NoValidProfile_ShowsNoValidAccountMessage()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((UserModel?)null);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

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
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((UserModel?)null);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

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
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        var user = CreateTestUser(hasCheckedInToday: false);
        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync(user);

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

        // First game succeeds, second game fails
        checkInApiMock.SetupSequence(x => x.GetAsync(It.IsAny<CheckInApiContext>()))
            .ReturnsAsync(Result<CheckInStatus>.Success(CheckInStatus.Success))
            .ReturnsAsync(Result<CheckInStatus>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

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

        // Verify user was NOT updated because not all check-ins succeeded
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_CheckedInYesterdayUtc8_AllowsCheckInToday()
    {
        // Arrange
        var (service, userRepositoryMock, gameRecordApiMock, checkInApiMock) = SetupMocks();

        // Create user who checked in yesterday (UTC+8)
        var cst = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cst);
        var yesterdayUtc8 = nowUtc8.AddDays(-1);
        var yesterdayUtc = TimeZoneInfo.ConvertTimeToUtc(yesterdayUtc8, cst);

        var user = new UserModel
        {
            Id = 1,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfile
                {
                    ProfileId = 1,
                    LtUid = 12345ul,
                    LToken = "test_token",
                    LastCheckIn = yesterdayUtc
                }
            ]
        };

        userRepositoryMock.Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
          .ReturnsAsync(user);

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

        var context = new CheckInApplicationContext(1)
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert - should proceed with check-in
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandText>().First().Content,
                Does.Contain("Check-in successful"));
        });

        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static (
        DailyCheckInService Service,
        Mock<IUserRepository> UserRepositoryMock,
        Mock<IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>> GameRecordApiMock,
        Mock<IApiService<CheckInStatus, CheckInApiContext>> CheckInApiMock
        ) SetupMocks()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var gameRecordApiMock = new Mock<IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>>();
        var checkInApiMock = new Mock<IApiService<CheckInStatus, CheckInApiContext>>();
        var loggerMock = new Mock<ILogger<DailyCheckInService>>();

        var service = new DailyCheckInService(
            userRepositoryMock.Object,
            gameRecordApiMock.Object,
            checkInApiMock.Object,
            loggerMock.Object);

        return (service, userRepositoryMock, gameRecordApiMock, checkInApiMock);
    }

    private static UserModel CreateTestUser(bool hasCheckedInToday)
    {
        var cst = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var nowUtc8 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cst);

        DateTime? lastCheckIn = null;
        if (hasCheckedInToday)
        {
            // Set check-in to earlier today (UTC+8)
            DateTime todayUtc8 = nowUtc8.Date.AddHours(10); // 10 AM today UTC+8
            lastCheckIn = TimeZoneInfo.ConvertTimeToUtc(todayUtc8, cst);
        }

        return new UserModel
        {
            Id = 1,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfile
                {
                    ProfileId = 1,
                    LtUid = 12345ul,
                    LToken = "test_token",
                    LastCheckIn = lastCheckIn
                }
            ]
        };
    }

    #endregion
}
