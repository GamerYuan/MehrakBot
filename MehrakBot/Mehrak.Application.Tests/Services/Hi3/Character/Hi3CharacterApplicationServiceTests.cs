#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hi3.Character;

[Parallelizable(ParallelScope.Self)]
public class Hi3CharacterApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hi3");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new Hi3CharacterApplicationContext(1, ("character", "Kiana"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "bad"
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
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new Hi3CharacterApplicationContext(1, ("character", "Kiana"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_CharacterNotFound_ReturnsEphemeralMessage()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync("Character_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        var context = new Hi3CharacterApplicationContext(1, ("character", "NonExistent"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("NonExistent"));
        });
    }

    [Test]
    public async Task ExecuteAsync_CharacterFoundByAlias_ReturnsSuccess()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, gameRoleApiMock, imageUpdaterMock, cardServiceMock, metricsMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var character = await LoadTestCharacterAsync("Character_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        characterCacheMock.Setup(x => x.GetAliases(Game.HonkaiImpact3))
            .Returns(new Dictionary<string, string> { { "AliasName", character.Avatar.Name } });

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(cardStream);

        var context = new Hi3CharacterApplicationContext(1, ("character", "AliasName"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
        });

        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
            character.Avatar.Name.ToLowerInvariant()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, imageUpdaterMock, cardServiceMock, _, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var character = await LoadTestCharacterAsync("Character_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        // Fail first image update
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(new MemoryStream());

        var context = new Hi3CharacterApplicationContext(1, ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        });
    }

    [Test]
    [TestCase("Character_TestData_1.json")]
    [TestCase("Character_TestData_2.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsCardAndTracksMetrics(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, imageUpdaterMock, cardServiceMock, metricsMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(cardStream);

        var context = new Hi3CharacterApplicationContext(1, ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().Any(), Is.True);
        });

        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
            character.Avatar.Name.ToLowerInvariant()), Times.Once);
    }

    [Test]
    [TestCase("Character_TestData_1.json")]
    public async Task ExecuteAsync_VerifyAllImagesUpdated(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, imageUpdaterMock, cardServiceMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(new MemoryStream());

        var context = new Hi3CharacterApplicationContext(1, ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var expectedCalls = character.Stigmatas.Count + character.Costumes.Count + 1; // weapon
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.Exactly(expectedCalls));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        userRepositoryMock.Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
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

        // Force early exit after UpdateGameUid: make character API fail
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new Hi3CharacterApplicationContext(1, ("character", "any"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.Is<UserDto>(u =>
            u.Id == 1ul
            && u.Profiles != null
            && u.Profiles.Any(p => p.LtUid == 1ul
                && p.GameUids != null
                && p.GameUids.ContainsKey(Game.HonkaiImpact3)
                && p.GameUids[Game.HonkaiImpact3].ContainsKey(Hi3Server.SEA.ToString())
                && p.GameUids[Game.HonkaiImpact3][Hi3Server.SEA.ToString()] == profile.GameUid)
        )), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User already has game uid stored
        userRepositoryMock.Setup(x => x.GetUserAsync(1ul))
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
                            { Game.HonkaiImpact3, new Dictionary<string, string> { { Hi3Server.SEA.ToString(), profile.GameUid } } }
                        }
                    }
                ]
            });

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new Hi3CharacterApplicationContext(1, ("character", "any"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, userRepositoryMock) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User not found
        userRepositoryMock.Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserDto?)null);

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new Hi3CharacterApplicationContext(1, ("character", "any"), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);

        // User exists but LtUid mismatch
        userRepositoryMock.Reset();
        userRepositoryMock.Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
            {
                Id = 1ul,
                Profiles = [new() { LtUid = 9999ul, LToken = "test" }]
            });

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Character_TestData_1.json")]
    [TestCase("Character_TestData_2.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, gameRoleApiMock) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        var context = new Hi3CharacterApplicationContext(DbTestHelper.Instance.GetUniqueUserId(),
            ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        });

        var attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null);
        Assert.That(attachment!.Content.Length, Is.GreaterThan(0));

        // Save card image
        var outputDir = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"Hi3CharacterIntegration_{character.Avatar.Name}.jpg");
        attachment.Content.Position = 0;
        await using var fs = File.Create(outputPath);
        await attachment.Content.CopyToAsync(fs);
    }

    [Test]
    [Explicit("Calls real API - run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        // NOTE: Placeholder for future real API integration if implemented.
        Assert.Pass("Real API integration not implemented for HI3 character list.");
    }

    #endregion

    #region Helper Methods

    private static (
        Hi3CharacterApplicationService Service,
        Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>> CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<Hi3CharacterDetail>> CardServiceMock,
        Mock<IMetricsService> MetricsServiceMock,
        Mock<IUserRepository> UserRepositoryMock
    ) SetupMocks()
    {
        var characterApiMock = new Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        characterCacheMock.Setup(x => x.GetAliases(Game.HonkaiImpact3)).Returns([]);
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<Hi3CharacterDetail>>();
        var metricsMock = new Mock<IMetricsService>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var loggerMock = new Mock<ILogger<Hi3CharacterApplicationService>>();

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(new MemoryStream());

        var service = new Hi3CharacterApplicationService(
            cardServiceMock.Object,
            characterApiMock.Object,
            imageUpdaterMock.Object,
            characterCacheMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, gameRoleApiMock, imageUpdaterMock, cardServiceMock, metricsMock, userRepositoryMock);
    }

    private static (
        Hi3CharacterApplicationService Service,
        Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>> CharacterApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock
    ) SetupIntegrationTest()
    {
        var characterApiMock = new Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();

        // Real card service + real image updater
        var cardService = new Hi3CharacterCardService(
            DbTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<Hi3CharacterCardService>>());

        cardService.InitializeAsync().Wait();

        var imageUpdaterService = new ImageUpdaterService(
            DbTestHelper.Instance.ImageRepository,
            CreateHttpClientFactory(),
            Mock.Of<ILogger<ImageUpdaterService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        characterCacheMock.Setup(x => x.GetAliases(Game.HonkaiImpact3)).Returns([]);

        var metricsMock = new Mock<IMetricsService>();
        var userRepositoryMock = new Mock<IUserRepository>();

        var service = new Hi3CharacterApplicationService(
            cardService,
            characterApiMock.Object,
            imageUpdaterService,
            characterCacheMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            Mock.Of<ILogger<Hi3CharacterApplicationService>>());

        return (service, characterApiMock, gameRoleApiMock);
    }

    private static IHttpClientFactory CreateHttpClientFactory()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return factoryMock.Object;
    }

    private static async Task<Hi3CharacterDetail> LoadTestCharacterAsync(string file)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var path = Path.Combine(TestDataPath, file);
        var json = await File.ReadAllTextAsync(path);
        var data = JsonSerializer.Deserialize<Hi3CharacterDetail>(json, options);
        return data ?? throw new InvalidOperationException($"Failed to deserialize {file}");
    }

    private static GameProfileDto CreateTestProfile() => new()
    {
        GameUid = "800000000",
        Nickname = "TestPlayer",
        Level = 88
    };

    #endregion
}
