#region

using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Common;

[Parallelizable(ParallelScope.Self), FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CodeRedeemApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory1 = null!;
    private TestDbContextFactory m_DbFactory2 = null!;

    [SetUp]
    public void Setup()
    {
        m_DbFactory1 = new TestDbContextFactory();
        m_DbFactory2 = new TestDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        m_DbFactory1.Dispose();
        m_DbFactory2.Dispose();
    }

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "TESTCODE123"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "invalid_token"
        };

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
    public async Task ExecuteAsync_SingleCode_RedeemsSuccessfully()
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Redeemed successfully", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "TESTCODE123"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("TESTCODE123"));
            Assert.That(content, Does.Contain("Redeemed successfully"));
        }

        Assert.That(await codeContext.Codes.AnyAsync(c => c.Code == "TESTCODE123" && c.Game == Game.Genshin), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_MultipleCodes_RedeemsAll()
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Redeemed successfully", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1, CODE2, CODE3"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("CODE1"));
            Assert.That(content, Does.Contain("CODE2"));
            Assert.That(content, Does.Contain("CODE3"));
        }

        codeRedeemApiMock.Verify(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()), Times.Exactly(3));
        Assert.That(await codeContext.Codes.CountAsync(), Is.EqualTo(3));
    }

    [Test]
    public async Task ExecuteAsync_NoCodeProvided_UsesRepositoryCodes()
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        await codeContext.Codes.AddRangeAsync(
            new CodeRedeemModel { Code = "CACHED1", Game = Game.Genshin },
            new CodeRedeemModel { Code = "CACHED2", Game = Game.Genshin }
        );
        await codeContext.SaveChangesAsync();

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Redeemed successfully", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", ""),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("CACHED1"));
            Assert.That(content, Does.Contain("CACHED2"));
        }

        codeRedeemApiMock.Verify(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()), Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_NoCodeProvidedAndNoCachedCodes_ReturnsNoCodesMessage()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", ""),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("No known codes found"));
        }
    }

    [Test]
    public async Task ExecuteAsync_CodeAlreadyRedeemed_ShowsInvalidStatus()
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeContext.Codes.Add(new CodeRedeemModel { Code = "ALREADYUSED", Game = Game.Genshin });
        await codeContext.SaveChangesAsync();
        codeContext.ChangeTracker.Clear();

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Code already redeemed", CodeStatus.Invalid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "ALREADYUSED"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.That(result.IsSuccess, Is.True);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("ALREADYUSED"));
            Assert.That(content, Does.Contain("Code already redeemed"));
        }

        Assert.That(await codeContext.Codes.AnyAsync(c => c.Code == "ALREADYUSED"), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_ApiError_ShowsErrorMessage()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError,
                "API temporarily unavailable"));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "ERRORCODE"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("ERRORCODE"));
            Assert.That(content, Does.Contain("An error occurred"));
        }
    }

    [Test]
    public async Task ExecuteAsync_MixedResults_SavesOnlySuccessful()
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.SetupSequence(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)))
            .ReturnsAsync(Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError, "Error"));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "SUCCESS, FAIL"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("SUCCESS"));
            Assert.That(content, Does.Contain("FAIL"));
        }

        Assert.That(await codeContext.Codes.CountAsync(), Is.EqualTo(1));
        Assert.That(await codeContext.Codes.AnyAsync(c => c.Code == "SUCCESS"), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_CodeNormalization_ConvertsToUppercaseAndTrims()
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", " lowercase123 "),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("LOWERCASE123"));
        }

        Assert.That(await codeContext.Codes.AnyAsync(c => c.Code == "LOWERCASE123"), Is.True);

        codeRedeemApiMock.Verify(
            x => x.GetAsync(It.Is<CodeRedeemApiContext>(c => c.Code == "LOWERCASE123")),
            Times.Once);
    }

    [Test]
    [TestCase(Game.Genshin)]
    [TestCase(Game.HonkaiStarRail)]
    [TestCase(Game.ZenlessZoneZero)]
    public async Task ExecuteAsync_DifferentGames_WorksCorrectly(Game game)
    {
        // Arrange
        var (service, codeContext, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, game, ("code", "GAMECODE"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        codeRedeemApiMock.Verify(
            x => x.GetAsync(It.Is<CodeRedeemApiContext>(c => c.Game == game)),
            Times.Once);
        Assert.That(await codeContext.Codes.AnyAsync(c => c.Game == game && c.Code == "GAMECODE"), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_EmptyCodeInList_IgnoresEmptyCode()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin,
            ("code", "CODE1, , CODE2, , CODE3"), ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test_token"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        codeRedeemApiMock.Verify(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()), Times.Exactly(3));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, userContext) = SetupMocks();

        SeedUserProfile(userContext, 1ul, 1, 12345ul);

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        codeRedeemApiMock
            .Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(new CodeRedeemResult("ok", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var gameUid = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(gameUid, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(gameUid!.GameUid, Is.EqualTo(profile.GameUid));
            Assert.That(gameUid.Region, Is.EqualTo(Server.Asia.ToString()));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        var userProfile = SeedUserProfile(userContext, 1ul, 1, 12345ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = userProfile.Id,
            Game = Game.Genshin,
            Region = Server.Asia.ToString(),
            GameUid = profile.GameUid
        });
        await userContext.SaveChangesAsync();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        codeRedeemApiMock
            .Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(new CodeRedeemResult("ok", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
            Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        codeRedeemApiMock
            .Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(new CodeRedeemResult("ok", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1"),
            ("server", Server.Asia.ToString()))
        {
            LtUid = 12345ul,
            LToken = "test"
        };

        // Act - user not found
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        // Case: user exists but no matching profile
        SeedUserProfile(userContext, 1ul, 2, 99999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    #endregion

    #region Helper Methods

    private (
        CodeRedeemApplicationService Service,
        CodeRedeemDbContext CodeContext,
        Mock<IApiService<CodeRedeemResult, CodeRedeemApiContext>> CodeRedeemApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var codeContext = m_DbFactory1.CreateDbContext<CodeRedeemDbContext>();
        var userContext = m_DbFactory2.CreateDbContext<UserDbContext>();

        var codeRedeemApiMock = new Mock<IApiService<CodeRedeemResult, CodeRedeemApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<CodeRedeemApplicationService>>();

        var service = new CodeRedeemApplicationService(
            codeContext,
            codeRedeemApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            loggerMock.Object);

        return (service, codeContext, codeRedeemApiMock, gameRoleApiMock, userContext);
    }

    private static GameProfileDto CreateTestProfile()
    {
        return new GameProfileDto
        {
            GameUid = "123456789",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static UserProfileModel SeedUserProfile(UserDbContext userContext, ulong userId, int profileId,
        ulong ltUid)
    {
        var user = new UserModel
        {
            Id = (long)userId,
            Timestamp = DateTime.UtcNow
        };

        var profile = new UserProfileModel
        {
            Id = profileId,
            User = user,
            UserId = user.Id,
            ProfileId = profileId,
            LtUid = (long)ltUid,
            LToken = "test"
        };

        user.Profiles.Add(profile);
        userContext.Users.Add(user);
        userContext.SaveChanges();

        return profile;
    }

    #endregion
}
