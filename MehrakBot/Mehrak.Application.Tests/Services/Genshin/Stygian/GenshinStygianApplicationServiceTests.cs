#region

using System.Text.Json;
using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.Stygian;

[Parallelizable(ParallelScope.Self)]
public class GenshinStygianApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> _, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository> _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
    public async Task ExecuteAsync_StygianApiError_ReturnsApiError()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
            Assert.That(result.ErrorMessage, Does.Contain("Stygian Onslaught data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_StygianNotUnlocked_ReturnsEphemeralMessage()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = new GenshinStygianInformation
        {
            IsUnlock = false,
            Data = null
        };

        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
                Does.Contain("not unlocked").IgnoreCase);
        });
    }

    [Test]
    public async Task ExecuteAsync_NoStygianData_ReturnsNoClearRecords()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = new GenshinStygianInformation
        {
            IsUnlock = true,
            Data =
            [
                new StygianData
                {
                    Single = new StygianChallengeData
                    {
                        HasData = false,
                        Challenge = null,
                        StygianBestRecord = null
                    },
                    Multi = new StygianChallengeData
                    {
                        HasData = false,
                        Challenge = null,
                        StygianBestRecord = null
                    }
                }
            ]
        };

        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService>? imageUpdaterMock, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        GenshinStygianInformation stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        // Make image update fail
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService>? imageUpdaterMock, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        GenshinStygianInformation stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
            Assert.That(
                result.Data.Components.OfType<CommandText>().Any(x => x.Content.Contains("Stygian Onslaught Summary")),
                Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService>? imageUpdaterMock, Mock<IUserRepository> _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        GenshinStygianInformation stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar images, side avatar images, and monster images were updated
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(d => d != null),
                It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository>? userRepositoryMock) = SetupMocks();

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

        // Force early exit after UpdateGameUid by making stygian API fail
        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository>? userRepositoryMock) = SetupMocks();

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
                                Game.Genshin,
                                new Dictionary<string, string> { { Server.Asia.ToString(), profile.GameUid } }
                            }
                        }
                    }
                ]
            });

        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository>? userRepositoryMock) = SetupMocks();

        GameProfileDto profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // Case: user not found
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserModel?)null);

        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
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
    [TestCase("Stygian_TestData_1.json")]
    [TestCase("Stygian_TestData_2.json")]
    [TestCase("Stygian_TestData_3.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        (GenshinStygianApplicationService? service, Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>? stygianApiMock, Mock<IApiService<GameProfileDto, GameRoleApiContext>>? gameRoleApiMock, Mock<IImageUpdaterService> _, Mock<IUserRepository> _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        GenshinStygianInformation stygianData = await LoadTestDataAsync(testDataFile);
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        var context = new GenshinStygianApplicationContext(MongoTestHelper.Instance.GetUniqueUserId(), ("server", Server.Asia.ToString()))
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
        var outputImagePath = Path.Combine(outputDirectory,
            $"StygianIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

        attachment.Content.Position = 0;
        await using FileStream fileStream = File.Create(outputImagePath);
        await attachment.Content.CopyToAsync(fileStream);
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
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

        GenshinStygianApplicationService service = SetupRealApiIntegrationTest();

        var context = new GenshinStygianApplicationContext(MongoTestHelper.Instance.GetUniqueUserId(), ("server", Server.Asia.ToString()))
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
            var outputImagePath = Path.Combine(outputDirectory, "StygianRealApi.jpg");

            attachment.Content.Position = 0;
            await using FileStream fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        GenshinStygianApplicationService Service,
        Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> StygianApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupMocks()
    {
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<StygianData>>();
        var stygianApiMock = new Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var loggerMock = new Mock<ILogger<GenshinStygianApplicationService>>();

        // Setup card service to return a valid stream
        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<StygianData>>()))
            .ReturnsAsync(cardStream);

        var service = new GenshinStygianApplicationService(
            imageUpdaterMock.Object,
            cardServiceMock.Object,
            stygianApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            loggerMock.Object);

        return (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, userRepositoryMock);
    }

    private static (
        GenshinStygianApplicationService Service,
        Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> StygianApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new GenshinStygianCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>());

        var stygianApiMock = new Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinStygianApplicationService>>();
        var userRepositoryMock = new Mock<IUserRepository>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new GenshinStygianApplicationService(
            imageUpdaterService,
            cardService,
            stygianApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, userRepositoryMock);
    }

    private static GenshinStygianApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new GenshinStygianCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Real Stygian API service
        var stygianApi = new GenshinStygianApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinStygianApiService>>());

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

        var service = new GenshinStygianApplicationService(
            imageUpdaterService,
            cardService,
            stygianApi,
            gameRoleApiService,
            userRepositoryMock.Object,
            Mock.Of<ILogger<GenshinStygianApplicationService>>());

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

    private static async Task<GenshinStygianInformation> LoadTestDataAsync(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        StygianData? data = JsonSerializer.Deserialize<StygianData>(json);

        Assert.That(data, Is.Not.Null);

        GenshinStygianInformation result = new()
        {
            IsUnlock = true,
            Data = [data]
        };

        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
