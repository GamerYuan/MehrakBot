#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.EndGame;

[Parallelizable(ParallelScope.Self)]
public class HsrEndGameApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>> _, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository> _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new HsrEndGameApplicationContext(1, HsrEndGameMode.PureFiction, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        });
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    public async Task ExecuteAsync_EndGameApiError_ReturnsApiError(HsrEndGameMode mode)
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new HsrEndGameApplicationContext(1, mode, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain($"{mode.GetString()} data"));
        });
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    public async Task ExecuteAsync_NoData_ReturnsNoClearRecords(HsrEndGameMode mode)
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var endGameData = new HsrEndInformation
        {
            HasData = false,
            Groups = [],
            AllFloorDetail = [],
            MaxFloor = "0",
            StarNum = 0,
            BattleNum = 0,
            MaxFloorId = 0
        };

        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        var context = new HsrEndGameApplicationContext(1, mode, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
        });
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError(HsrEndGameMode mode)
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService>? imageUpdaterMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var testDataFile = mode == HsrEndGameMode.PureFiction ? "Pf_TestData_1.json" : "As_TestData_1.json";
        HsrEndInformation endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        // Make image update fail
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new HsrEndGameApplicationContext(1, mode, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        });
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_1.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_1.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(HsrEndGameMode mode, string testDataFile)
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService>? imageUpdaterMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>>? cardServiceMock, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        HsrEndInformation endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrEndInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrEndGameApplicationContext(1, mode, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>()
                    .Any(x => x.Content.Contains($"{mode.GetString()} Summary")),
                Is.True);
        });
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_1.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_1.json")]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly(HsrEndGameMode mode, string testDataFile)
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService>? imageUpdaterMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>>? cardServiceMock, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        HsrEndInformation endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrEndInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrEndGameApplicationContext(1, mode, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar images and buff images were updated
        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository>? userRepositoryMock) = SetupMocks();

        GameProfileDto profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User exists with matching profile but no stored GameUids
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserModel
            {
                Id = 1ul,
                Profiles =
                [
                    new()
                    {
                        LtUid = 1ul,
                        LToken = "test",
                        GameUids = null
                    }
                ]
            });

        // Force early exit after UpdateGameUid by making API fail
        endGameApiMock
            .Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new HsrEndGameApplicationContext(1, HsrEndGameMode.PureFiction, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: repository should persist updated user with stored game uid
        userRepositoryMock.Verify(
            x => x.CreateOrUpdateUserAsync(It.Is<UserModel>(u =>
                u.Id == 1ul
                && u.Profiles != null
                && u.Profiles.Any(p => p.LtUid == 1ul
                                       && p.GameUids != null
                                       && p.GameUids.ContainsKey(Game.HonkaiStarRail)
                                       && p.GameUids[Game.HonkaiStarRail].ContainsKey(Server.Asia.ToString())
                                       && p.GameUids[Game.HonkaiStarRail][Server.Asia.ToString()] == profile.GameUid)
            )),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository>? userRepositoryMock) = SetupMocks();

        GameProfileDto profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User exists with game uid already stored for this game/server
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserModel
            {
                Id = 1ul,
                Profiles =
                [
                    new()
                    {
                        LtUid = 1ul,
                        LToken = "test",
                        GameUids = new Dictionary<Game, Dictionary<string, string>>
                        {
                            {
                                Game.HonkaiStarRail,
                                new Dictionary<string, string> { { Server.Asia.ToString(), profile.GameUid } }
                            }
                        }
                    }
                ]
            });

        endGameApiMock
            .Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new HsrEndGameApplicationContext(1, HsrEndGameMode.PureFiction, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
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
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<ICardService<HsrEndInformation>> _, Mock<IUserRepository>? userRepositoryMock) = SetupMocks();

        GameProfileDto profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // Case: user not found
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserModel?)null);

        endGameApiMock
            .Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new HsrEndGameApplicationContext(1, HsrEndGameMode.PureFiction, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
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
                Profiles =
                [
                    new() { LtUid = 99999ul, LToken = "test" }
                ]
            });

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserModel>()), Times.Never);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_1.json")]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_2.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_1.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_2.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(HsrEndGameMode mode, string testDataFile)
    {
        // Arrange
        (HsrEndGameApplicationService? service, Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>? endGameApiMock, Mock<IImageUpdaterService> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IUserRepository> _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        HsrEndInformation endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        var context = new HsrEndGameApplicationContext(MongoTestHelper.Instance.GetUniqueUserId(), mode, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        });
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));

        CommandAttachment? attachment = result.Data.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(attachment!.Content.Length, Is.GreaterThan(0), "Expected a non-empty card image");

        // Save the generated card for manual inspection
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(
            outputDirectory,
            $"HsrEndGameIntegration_{mode}_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

        attachment.Content.Position = 0;
        await using FileStream fileStream = File.Create(outputImagePath);
        await attachment.Content.CopyToAsync(fileStream);
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow(HsrEndGameMode mode)
    {
        // This test requires real credentials and should only be run manually
        // It demonstrates the full integration with the actual HoYoLab API

        IConfigurationSection config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        HsrEndGameApplicationService service = SetupRealApiIntegrationTest();

        var context = new HsrEndGameApplicationContext(MongoTestHelper.Instance.GetUniqueUserId(), mode, ("server", Server.Asia.ToString()))
        {
            LtUid = testLtUid,
            LToken = testLToken!
        };

        // Act
        CommandResult result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True, $"API call failed: {result.ErrorMessage}");

        if (result.IsSuccess)
        {
            CommandAttachment? attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(attachment!.Content.Length, Is.GreaterThan(0));

            // Save output
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, $"HsrEndGameRealApi_{mode}.jpg");

            attachment.Content.Position = 0;
            await using FileStream fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        HsrEndGameApplicationService Service,
        Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>> EndGameApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<HsrEndInformation>> CardServiceMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrEndInformation>>();
        var endGameApiMock = new Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var loggerMock = new Mock<ILogger<HsrEndGameApplicationService>>();

        var service = new HsrEndGameApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            endGameApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            loggerMock.Object);

        return (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, userRepositoryMock);
    }

    private static (
        HsrEndGameApplicationService Service,
        Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>> EndGameApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new HsrEndGameCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrEndGameCardService>>());

        var endGameApiMock = new Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var loggerMock = new Mock<ILogger<HsrEndGameApplicationService>>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new HsrEndGameApplicationService(
            cardService,
            imageUpdaterService,
            endGameApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, userRepositoryMock);
    }

    private static HsrEndGameApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new HsrEndGameCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrEndGameCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Real End Game API service
        var endGameApiService = new HsrEndGameApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<HsrEndGameApiService>>());

        // Real game role API service
        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        // Real image updater service
        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var userRepositoryMock = new Mock<IUserRepository>();

        var service = new HsrEndGameApplicationService(
            cardService,
            imageUpdaterService,
            endGameApiService,
            gameRoleApiService,
            userRepositoryMock.Object,
            Mock.Of<ILogger<HsrEndGameApplicationService>>());

        return service;
    }

    private static GameProfileDto CreateTestProfile()
    {
        return new GameProfileDto
        {
            GameUid = "800000000",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static async Task<HsrEndInformation> LoadTestDataAsync(string filename)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        HsrEndInformation? result = JsonSerializer.Deserialize<HsrEndInformation>(json, options);
        if (result == null) throw new InvalidOperationException($"Failed to deserialize {filename}");

        return result;
    }

    #endregion
}
