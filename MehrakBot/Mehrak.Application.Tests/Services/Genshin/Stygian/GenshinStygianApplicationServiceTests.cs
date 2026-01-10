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
        var (service, _, gameRoleApiMock, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

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
        var (service, stygianApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

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
        var result = await service.ExecuteAsync(context);

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
        var (service, stygianApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

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
        var result = await service.ExecuteAsync(context);

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
        var (service, stygianApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

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
        var result = await service.ExecuteAsync(context);

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
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

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
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
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
        var result = await service.ExecuteAsync(context);

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

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
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
        var (service, stygianApiMock, gameRoleApiMock, _, attachmentStorageMock, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
            {
                Id = 1ul,
                Profiles =
                [
                    new()
                    {
                        LtUid = 1ul,
                        LToken = "test"
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

        userRepositoryMock.Verify(
            x => x.CreateOrUpdateUserAsync(It.Is<UserDto>(u =>
                u.Id == 1ul
                && u.Profiles != null
                && u.Profiles.Any(p => p.LtUid == 1ul
                                       && p.GameUids != null
                                       && p.GameUids.ContainsKey(Game.Genshin)
                                       && p.GameUids[Game.Genshin].ContainsKey(Server.Asia.ToString())
                                       && p.GameUids[Game.Genshin][Server.Asia.ToString()] == profile.GameUid)
            )),
            Times.Once);

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, attachmentStorageMock, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
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

        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, attachmentStorageMock, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserDto?)null);

        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinStygianApplicationContext(1, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);

        userRepositoryMock.Reset();
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
            {
                Id = 1ul,
                Profiles = [new() { LtUid = 99999ul, LToken = "test" }]
            });

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, storedAttachments) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync(testDataFile);
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = new GenshinStygianApplicationContext(DbTestHelper.Instance.GetUniqueUserId(), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        });
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));

        var attachment = result.Data.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(!string.IsNullOrWhiteSpace(attachment!.FileName));

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment.FileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        if (storedAttachments.TryGetValue(attachment.FileName, out var stored))
        {
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory,
                $"StygianIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

            stored.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await stored.CopyToAsync(fileStream);
        }
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var (service, storedAttachments) = SetupRealApiIntegrationTest();

        var context = new GenshinStygianApplicationContext(DbTestHelper.Instance.GetUniqueUserId(), ("server", Server.Asia.ToString()))
        {
            LtUid = testLtUid,
            LToken = testLToken!
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True, $"API call failed: {result.ErrorMessage}");

        if (result.IsSuccess)
        {
            var attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(storedAttachments.TryGetValue(attachment!.FileName, out var storedStream), Is.True);
            Assert.That(storedStream!.Length, Is.GreaterThan(0));

            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, "StygianRealApi.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        GenshinStygianApplicationService Service,
        Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> StygianApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupMocks()
    {
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<StygianData>>();
        var stygianApiMock = new Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinStygianApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<StygianData>>()))
            .ReturnsAsync(cardStream);

        var service = new GenshinStygianApplicationService(
            imageUpdaterMock.Object,
            cardServiceMock.Object,
            stygianApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, userRepositoryMock);
    }

    private static (
        GenshinStygianApplicationService Service,
        Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> StygianApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Dictionary<string, MemoryStream> StoredAttachments
        ) SetupIntegrationTest()
    {
        var cardService = new GenshinStygianCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>());

        var stygianApiMock = new Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            DbTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinStygianApplicationService>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var storedAttachments = new Dictionary<string, MemoryStream>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, Stream stream, CancellationToken _) =>
            {
                MemoryStream copy = new();
                if (stream.CanSeek) stream.Position = 0;
                stream.CopyTo(copy);
                copy.Position = 0;
                storedAttachments[name] = copy;
                return true;
            });

        cardService.InitializeAsync().Wait();

        var service = new GenshinStygianApplicationService(
            imageUpdaterService,
            cardService,
            stygianApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, storedAttachments);
    }

    private static (GenshinStygianApplicationService Service, Dictionary<string, MemoryStream> StoredAttachments) SetupRealApiIntegrationTest()
    {
        var cardService = new GenshinStygianCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var stygianApi = new GenshinStygianApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinStygianApiService>>());

        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        var imageUpdaterService = new ImageUpdaterService(
            DbTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        cardService.InitializeAsync().Wait();

        var userRepositoryMock = new Mock<IUserRepository>();
        var storedAttachments = new Dictionary<string, MemoryStream>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => storedAttachments.ContainsKey(name));
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, Stream stream, CancellationToken _) =>
            {
                MemoryStream copy = new();
                if (stream.CanSeek) stream.Position = 0;
                stream.CopyTo(copy);
                copy.Position = 0;
                storedAttachments[name] = copy;
                return true;
            });

        var service = new GenshinStygianApplicationService(
            imageUpdaterService,
            cardService,
            stygianApi,
            gameRoleApiService,
            userRepositoryMock.Object,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<GenshinStygianApplicationService>>());

        return (service, storedAttachments);
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
        var data = JsonSerializer.Deserialize<StygianData>(json);

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
