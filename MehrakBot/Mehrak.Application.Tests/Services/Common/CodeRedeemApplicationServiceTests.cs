#region

using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Common;

[Parallelizable(ParallelScope.Self)]
public class CodeRedeemApplicationServiceTests
{
    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "TESTCODE123"))
        {
            LtUid = 12345ul,
            LToken = "invalid_token",
            Server = Server.Asia
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
    public async Task ExecuteAsync_SingleCode_RedeemsSuccessfully()
    {
        // Arrange
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Redeemed successfully", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "TESTCODE123"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("TESTCODE123"));
            Assert.That(content, Does.Contain("Redeemed successfully"));
        });

        // Verify code was saved to repository
        codeRepositoryMock.Verify(
            x => x.AddCodesAsync(Game.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(d =>
                    d.ContainsKey("TESTCODE123") && d["TESTCODE123"] == CodeStatus.Valid)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_MultipleCodes_RedeemsAll()
    {
        // Arrange
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Redeemed successfully", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1, CODE2, CODE3"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("CODE1"));
            Assert.That(content, Does.Contain("CODE2"));
            Assert.That(content, Does.Contain("CODE3"));
        });

        // Verify all codes were attempted
        codeRedeemApiMock.Verify(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()), Times.Exactly(3));

        // Verify codes were saved
        codeRepositoryMock.Verify(
            x => x.AddCodesAsync(Game.Genshin, It.Is<Dictionary<string, CodeStatus>>(d => d.Count == 3)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoCodeProvided_UsesRepositoryCodes()
    {
        // Arrange
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var cachedCodes = new List<string> { "CACHED1", "CACHED2" };
        codeRepositoryMock.Setup(x => x.GetCodesAsync(Game.Genshin))
            .ReturnsAsync(cachedCodes);

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Redeemed successfully", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", ""))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("CACHED1"));
            Assert.That(content, Does.Contain("CACHED2"));
        });

        codeRepositoryMock.Verify(x => x.GetCodesAsync(Game.Genshin), Times.Once);
        codeRedeemApiMock.Verify(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()), Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_NoCodeProvidedAndNoCachedCodes_ReturnsNoCodesMessage()
    {
        // Arrange
        var (service, codeRepositoryMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRepositoryMock.Setup(x => x.GetCodesAsync(Game.Genshin))
            .ReturnsAsync([]);

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", ""))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("No known codes found"));
        });
    }

    [Test]
    public async Task ExecuteAsync_CodeAlreadyRedeemed_ShowsInvalidStatus()
    {
        // Arrange
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Code already redeemed", CodeStatus.Invalid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "ALREADYUSED"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("ALREADYUSED"));
            Assert.That(content, Does.Contain("Code already redeemed"));
        });

        // Verify invalid code is still saved
        codeRepositoryMock.Verify(
            x => x.AddCodesAsync(Game.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(d =>
                    d.ContainsKey("ALREADYUSED") && d["ALREADYUSED"] == CodeStatus.Invalid)),
            Times.Once);
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

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "ERRORCODE"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("ERRORCODE"));
            Assert.That(content, Does.Contain("An error occurred"));
        });
    }

    [Test]
    public async Task ExecuteAsync_MixedResults_SavesOnlySuccessful()
    {
        // Arrange
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        // First code succeeds, second fails
        codeRedeemApiMock.SetupSequence(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)))
            .ReturnsAsync(Result<CodeRedeemResult>.Failure(StatusCode.ExternalServerError, "Error"));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "SUCCESS, FAIL"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("SUCCESS"));
            Assert.That(content, Does.Contain("FAIL"));
        });

        // Only successful code should be saved
        codeRepositoryMock.Verify(
            x => x.AddCodesAsync(Game.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(d => d.Count == 1 && d.ContainsKey("SUCCESS"))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CodeNormalization_ConvertsToUppercaseAndTrims()
    {
        // Arrange
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", " lowercase123 "))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            var content = result.Data!.Components.OfType<CommandText>().First().Content;
            Assert.That(content, Does.Contain("LOWERCASE123")); // Normalized to uppercase
        });

        // Verify normalized code was saved
        codeRepositoryMock.Verify(
            x => x.AddCodesAsync(Game.Genshin,
                It.Is<Dictionary<string, CodeStatus>>(d => d.ContainsKey("LOWERCASE123"))),
            Times.Once);

        // Verify API was called with normalized code
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
        var (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        codeRedeemApiMock.Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(
                new CodeRedeemResult("Success", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, game, ("code", "GAMECODE"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        // Verify correct game was used
        codeRepositoryMock.Verify(x => x.AddCodesAsync(game, It.IsAny<Dictionary<string, CodeStatus>>()),
            Times.Once);
        codeRedeemApiMock.Verify(
            x => x.GetAsync(It.Is<CodeRedeemApiContext>(c => c.Game == game)),
            Times.Once);
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

        // Code list with empty entries
        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1, , CODE2, , CODE3"))
        {
            LtUid = 12345ul,
            LToken = "test_token",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        // Only3 codes should be redeemed (empty ones ignored)
        codeRedeemApiMock.Verify(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()), Times.Exactly(3));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, userRepositoryMock) = SetupMocks();

        // Game profile from API
        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User exists with matching profile but no stored GameUids
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserModel
            {
                Id = 1ul,
                Profiles = new List<UserProfile>
                {
                    new()
                    {
                        LtUid = 12345ul,
                        LToken = "test",
                        GameUids = null
                    }
                }
            });

        // Redeem API returns success to progress flow
        codeRedeemApiMock
            .Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(new CodeRedeemResult("ok", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1"))
        {
            LtUid = 12345ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: repository should persist updated user with stored game uid
        userRepositoryMock.Verify(
            x => x.CreateOrUpdateUserAsync(It.Is<UserModel>(u =>
                u.Id == 1ul
                && u.Profiles != null
                && u.Profiles.Any(p => p.LtUid == 12345ul
                                       && p.GameUids != null
                                       && p.GameUids.ContainsKey(Game.Genshin)
                                       && p.GameUids[Game.Genshin].ContainsKey(Server.Asia.ToString())
                                       && p.GameUids[Game.Genshin][Server.Asia.ToString()] == profile.GameUid)
            )),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User exists with game uid already stored for this game/server
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserModel
            {
                Id = 1ul,
                Profiles = new List<UserProfile>
                {
                    new()
                    {
                        LtUid = 12345ul,
                        LToken = "test",
                        GameUids = new Dictionary<Game, Dictionary<string, string>>
                        {
                            {
                                Game.Genshin, new Dictionary<string, string>
                                {
                                    { Server.Asia.ToString(), profile.GameUid }
                                }
                            }
                        }
                    }
                }
            });

        codeRedeemApiMock
            .Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(new CodeRedeemResult("ok", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1"))
        {
            LtUid = 12345ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: no persistence since it was already stored
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, _, codeRedeemApiMock, gameRoleApiMock, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // Case: user not found
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserModel?)null);

        codeRedeemApiMock
            .Setup(x => x.GetAsync(It.IsAny<CodeRedeemApiContext>()))
            .ReturnsAsync(Result<CodeRedeemResult>.Success(new CodeRedeemResult("ok", CodeStatus.Valid)));

        var context = new CodeRedeemApplicationContext(1, Game.Genshin, ("code", "CODE1"))
        {
            LtUid = 12345ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: no persistence
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Never);

        // Case: user exists but no matching profile
        userRepositoryMock.Reset();
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserModel
            {
                Id = 1ul,
                Profiles = new List<UserProfile>
                {
                    new() { LtUid = 99999ul, LToken = "test" }
                }
            });

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private static (
        CodeRedeemApplicationService Service,
        Mock<ICodeRedeemRepository> CodeRepositoryMock,
        Mock<IApiService<CodeRedeemResult, CodeRedeemApiContext>> CodeRedeemApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupMocks()
    {
        var codeRepositoryMock = new Mock<ICodeRedeemRepository>();
        var codeRedeemApiMock = new Mock<IApiService<CodeRedeemResult, CodeRedeemApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var loggerMock = new Mock<ILogger<CodeRedeemApplicationService>>();

        var service = new CodeRedeemApplicationService(
            codeRepositoryMock.Object,
            codeRedeemApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            loggerMock.Object);

        return (service, codeRepositoryMock, codeRedeemApiMock, gameRoleApiMock, userRepositoryMock);
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

    #endregion
}
