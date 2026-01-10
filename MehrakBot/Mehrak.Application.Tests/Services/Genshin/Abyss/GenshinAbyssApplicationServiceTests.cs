#region

using System.Text.Json;
using Mehrak.Application.Services.Genshin.Abyss;
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

namespace Mehrak.Application.Tests.Services.Genshin.Abyss;

[Parallelizable(ParallelScope.Self)]
public class GenshinAbyssApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
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
    public async Task ExecuteAsync_AbyssApiError_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
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
            Assert.That(result.ErrorMessage, Does.Contain("Spiral Abyss data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_FloorNotFound_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = new GenshinAbyssInformation
        {
            Floors =
            [
                new()
                {
                    Index = 11,
                    Levels = []
                }
            ],
            RevealRank = [],
            DefeatRank = [],
            DamageRank = [],
            TakeDamageRank = [],
            NormalSkillRank = [],
            EnergySkillRank = []
        };

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
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
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, characterApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "Character API Error"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
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
            Assert.That(result.ErrorMessage, Does.Contain("Character List"));
        });
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        // Make image update fail
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
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
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        });
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock, _, attachmentStorageMock) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinAbyssInformation>>() ))
            .ReturnsAsync(cardStream);

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
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
                result.Data.Components.OfType<CommandText>().Any(x => x.Content.Contains("Spiral Abyss Summary")),
                Is.True);
        });

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinAbyssInformation>>() ))
            .ReturnsAsync(cardStream);

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert Verify avatar images were updated (for battles + rank avatars)
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(d => d != null),
                It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, userRepositoryMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User with matching profile and no stored game uids
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

        // Force early exit after UpdateGameUid by making abyss API fail
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert persisted
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
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, userRepositoryMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
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
                                Game.Genshin, new Dictionary<string, string>
                                    { { Server.Asia.ToString(), profile.GameUid } }
                            }
                        }
                    }
                ]
            });

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert not persisted
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, userRepositoryMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // Case1: user missing
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserDto?)null);

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);

        // Case2: user present but no matching profile
        userRepositoryMock.Reset();
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
            {
                Id = 1ul,
                Profiles = [new() { LtUid = 99999ul, LToken = "x" }]
            });

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Abyss_TestData_1.json", 12u)]
    [TestCase("Abyss_TestData_2.json", 12u)]
    [TestCase("Abyss_TestData_3.json", 12u)]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, uint floor)
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, _, attachmentStorageMock, storedAttachments) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>(testDataFile);
        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = new GenshinAbyssApplicationContext(
            DbTestHelper.Instance.GetUniqueUserId(),
            ("floor", (object)floor), ("server", Server.Asia.ToString()))
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

        if (storedAttachments.TryGetValue(attachment!.FileName, out var stored))
        {
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory,
                $"AbyssIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}_Floor{floor}.jpg");

            stored.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await stored.CopyToAsync(fileStream);
        }
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        // This test requires real credentials and should only be run manually
        // It demonstrates the full integration with the actual HoYoLab API

        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];
        const uint floor = 12u;

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var (service, storedAttachments) = SetupRealApiIntegrationTest();

        var context = new GenshinAbyssApplicationContext(
            DbTestHelper.Instance.GetUniqueUserId(),
            ("floor", (object)floor), ("server", Server.Asia.ToString()))
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

            // Save output
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, $"AbyssRealApi_Floor{floor}.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        GenshinAbyssApplicationService Service,
        Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>> AbyssApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<ICardService<GenshinAbyssInformation>>
        CardServiceMock,
        Mock<IUserRepository> UserRepositoryMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock
        ) SetupMocks()
    {
        var cardServiceMock =
            new Mock<ICardService<GenshinAbyssInformation>>();
        var abyssApiMock = new Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>>();
        var characterApiMock =
            new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinAbyssApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new GenshinAbyssApplicationService(
            cardServiceMock.Object,
            abyssApiMock.Object,
            characterApiMock.Object,
            imageUpdaterMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock,
            userRepositoryMock, attachmentStorageMock);
    }

    private static (
        GenshinAbyssApplicationService Service,
        Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>> AbyssApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        ICardService<GenshinAbyssInformation> CardService,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Dictionary<string, MemoryStream> StoredAttachments
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new GenshinAbyssCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinAbyssCardService>>());

        var abyssApiMock = new Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>>();
        var characterApiMock =
            new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            DbTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinAbyssApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var storedAttachments = new Dictionary<string, MemoryStream>();
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

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new GenshinAbyssApplicationService(
            cardService,
            abyssApiMock.Object,
            characterApiMock.Object,
            imageUpdaterService,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardService, attachmentStorageMock, storedAttachments);
    }

    private static (GenshinAbyssApplicationService Service, Dictionary<string, MemoryStream> StoredAttachments) SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new GenshinAbyssCardService(DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinAbyssCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Simple in-memory cache service for tests
        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<GenshinBasicCharacterData>?)null);

        // Real character API service
        var characterApiService = new GenshinCharacterApiService(
            cacheServiceMock.Object,
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinCharacterApiService>>());

        var abyssApi = new GenshinAbyssApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinAbyssApiService>>());

        // Real game role API service
        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        // Real image updater service
        var imageUpdaterService = new ImageUpdaterService(
            DbTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        // Initialize card service
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

        var service = new GenshinAbyssApplicationService(
            cardService,
            abyssApi,
            characterApiService,
            imageUpdaterService,
            gameRoleApiService,
            userRepositoryMock.Object,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<GenshinAbyssApplicationService>>());

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

    private static List<GenshinBasicCharacterData> CreateTestCharacterList()
    {
        return
        [
            new GenshinBasicCharacterData
            {
                Id = 10000032,
                Icon = "",
                Name = "Bennett",
                Element = "Pyro",
                Level = 80,
                Rarity = 4,
                ActivedConstellationNum = 6,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000037,
                Icon = "",
                Name = "Ganyu",
                Element = "Cryo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 1,
                Weapon = new Weapon { Icon = "", Name = "Bow" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000063,
                Icon = "",
                Name = "Shenhe",
                Element = "Cryo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 2,
                Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000089,
                Icon = "",
                Name = "Mika",
                Element = "Cryo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 6,
                Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000103,
                Icon = "",
                Name = "Furina",
                Element = "Hydro",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 3,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000106,
                Icon = "",
                Name = "Clorinde",
                Element = "Electro",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 0,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000107,
                Icon = "",
                Name = "Emilie",
                Element = "Dendro",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 4,
                Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000112,
                Icon = "",
                Name = "Chasca",
                Element = "Anemo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 5,
                Weapon = new Weapon { Icon = "", Name = "Bow" }
            }
        ];
    }

    private static async Task<T> LoadTestDataAsync<T>(string filename) where T : class
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<T>(json);
        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
