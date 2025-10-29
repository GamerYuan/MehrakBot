#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Zzz.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Zzz.Character;

[Parallelizable(ParallelScope.Self)]
public class ZzzCharacterApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new ZzzCharacterApplicationContext(1, ("character", "Jane"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
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
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError,
            "API Error"));

        var context = new ZzzCharacterApplicationContext(1, ("character", "Jane"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
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
    public async Task ExecuteAsync_CharacterNotFound_ReturnsNotFoundMessage()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        var context = new ZzzCharacterApplicationContext(1, ("character", "NonExistentCharacter"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
               Does.Contain("NonExistentCharacter"));
        });
    }

    [Test]
    public async Task ExecuteAsync_CharacterFoundByAlias_ReturnsSuccess()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, imageRepositoryMock, imageUpdaterMock, gameRoleApiMock,
             _, cardServiceMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        // Setup alias mapping
        var aliases = new Dictionary<string, string> { { "JD", "Jane" } };
        characterCacheMock.Setup(x => x.GetAliases(Game.ZenlessZoneZero))
            .Returns(aliases);

        var fullCharData = await LoadTestDataAsync("Jane_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<ZzzFullAvatarData>.Success(fullCharData));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzFullAvatarData>>()))
            .ReturnsAsync(cardStream);

        var context = new ZzzCharacterApplicationContext(1, ("character", "JD"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, imageRepositoryMock, _, gameRoleApiMock, wikiApiMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        var fullCharData = await LoadTestDataAsync("Jane_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<ZzzFullAvatarData>.Success(fullCharData));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = new ZzzCharacterApplicationContext(1, ("character", "Jane"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character Image"));
        });
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsBotError()
    {
        // Arrange
        var (service, characterApiMock, _, imageRepositoryMock, imageUpdaterMock, gameRoleApiMock, _, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        var fullCharData = await LoadTestDataAsync("Jane_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<ZzzFullAvatarData>.Success(fullCharData));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Make image update fail
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new ZzzCharacterApplicationContext(1, ("character", "Jane"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
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
    [TestCase("Jane_TestData.json", "Jane")]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(string testDataFile, string characterName)
    {
        // Arrange
        var (service, characterApiMock, _, imageRepositoryMock, imageUpdaterMock, gameRoleApiMock, _,
            cardServiceMock, metricsMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
           .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        var fullCharData = await LoadTestDataAsync(testDataFile);
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<ZzzFullAvatarData>.Success(fullCharData));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzFullAvatarData>>()))
            .ReturnsAsync(cardStream);

        var context = new ZzzCharacterApplicationContext(1, ("character", characterName))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
        });

        // Verify metrics tracked
        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.ZenlessZoneZero), characterName.ToLowerInvariant()),
            Times.Once);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Jane_TestData.json", "Jane")]
    [TestCase("Miyabi_TestData.json", "Miyabi")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, string characterName)
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        var fullCharData = await LoadTestDataAsync(testDataFile);
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<ZzzFullAvatarData>.Success(fullCharData));

        var context = new ZzzCharacterApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(),
            ("character", characterName))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
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
        Assert.That(attachment!.Content.Length, Is.GreaterThan(0), "Expected a non-empty card image");

        // Save the generated card for manual inspection
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(
            outputDirectory,
            $"ZzzCharacterIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}_{characterName}.jpg");

        attachment.Content.Position = 0;
        await using var fileStream = File.Create(outputImagePath);
        await attachment.Content.CopyToAsync(fileStream);
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        // This test requires real credentials and should only be run manually
        // It demonstrates the full integration with the actual HoYoLab API

        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        ulong testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        string? testLToken = config["LToken"];
        string characterName = "Jane"; // Replace with a character you own

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var service = SetupRealApiIntegrationTest();

        var context = new ZzzCharacterApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(),
            ("character", characterName))
        {
            LtUid = testLtUid,
            LToken = testLToken!,
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True, $"API call failed: {result.ErrorMessage}");

        if (result.IsSuccess)
        {
            var attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(attachment!.Content.Length, Is.GreaterThan(0));

            // Save output
            string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            string outputImagePath = Path.Combine(outputDirectory, $"ZzzCharacterRealApi_{characterName}.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
    ZzzCharacterApplicationService Service,
        Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>> CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<ICardService<ZzzFullAvatarData>> CardServiceMock,
        Mock<IMetricsService> MetricsMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<ZzzFullAvatarData>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData,
            CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var metricsMock = new Mock<IMetricsService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<ZzzCharacterApplicationService>>();

        // Setup default empty aliases
        characterCacheMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        var service = new ZzzCharacterApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            imageRepositoryMock.Object,
            characterApiMock.Object,
            characterCacheMock.Object,
            wikiApiMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, imageRepositoryMock, imageUpdaterMock, gameRoleApiMock,
            wikiApiMock, cardServiceMock, metricsMock);
    }

    private static (
        ZzzCharacterApplicationService Service,
        Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>> CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new ZzzCharacterCardService(
                    MongoTestHelper.Instance.ImageRepository,
              Mock.Of<ILogger<ZzzCharacterCardService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<ZzzBasicAvatarData,
            ZzzFullAvatarData, CharacterApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();

        // Setup default empty aliases
        characterCacheMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns(new Dictionary<string, string>());

        // Mock image repository to always return true for FileExists (images are in MongoDB)
        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<ZzzCharacterApplicationService>>();

        // Mock wiki API
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();

        // Mock metrics
        var metricsMock = new Mock<IMetricsService>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new ZzzCharacterApplicationService(
            cardService,
            imageUpdaterService,
            imageRepositoryMock.Object,
            characterApiMock.Object,
            characterCacheMock.Object,
            wikiApiMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, imageRepositoryMock, gameRoleApiMock);
    }

    private static ZzzCharacterApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new ZzzCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzCharacterCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Simple in-memory cache service for tests
        var cacheServiceMock = new Mock<ICacheService>();
        // Return null for all cache gets (no caching behavior in test)
        cacheServiceMock.Setup(x => x.GetAsync<ZzzBasicAvatarData>(It.IsAny<string>()))
            .ReturnsAsync((ZzzBasicAvatarData?)null);
        cacheServiceMock.Setup(x => x.GetAsync<ZzzFullAvatarData>(It.IsAny<string>()))
            .ReturnsAsync((ZzzFullAvatarData?)null);

        // Real character API service
        var characterApiService = new ZzzCharacterApiService(
            cacheServiceMock.Object,
            httpClientFactory.Object,
            Mock.Of<ILogger<ZzzCharacterApiService>>());

        // Mock character cache service (we don't need real character/alias data from DB)
        var characterCacheServiceMock = new Mock<ICharacterCacheService>();
        characterCacheServiceMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        // Real wiki API service
        var wikiApiService = new WikiApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<WikiApiService>>());

        // Real game role API service
        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        // Real image updater service
        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        // Mock metrics (don't want to send real metrics in tests)
        var metricsMock = new Mock<IMetricsService>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new ZzzCharacterApplicationService(
            cardService,
            imageUpdaterService,
            MongoTestHelper.Instance.ImageRepository,
            characterApiService,
            characterCacheServiceMock.Object,
            wikiApiService,
            metricsMock.Object,
            gameRoleApiService,
            Mock.Of<ILogger<ZzzCharacterApplicationService>>());

        return service;
    }

    private static GameProfileDto CreateTestProfile()
    {
        return new GameProfileDto
        {
            GameUid = "1000000000",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static List<ZzzBasicAvatarData> CreateBasicCharacterList()
    {
        return
        [
            new ZzzBasicAvatarData
            {
                Id = 1,
                Name = "Jane",
                FullName = "Jane Doe",
                CampName = "Unknown",
                Rarity = "S",
                GroupIconPath = "icon",
                HollowIconPath = "hollow",
                RoleSquareUrl = "url",
                AwakenState = "0"
            },
            new ZzzBasicAvatarData
            {
                Id = 2,
                Name = "Miyabi",
                FullName = "Hoshimi Miyabi",
                CampName = "Section 6",
                Rarity = "S",
                GroupIconPath = "icon",
                HollowIconPath = "hollow",
                RoleSquareUrl = "url",
                AwakenState = "0"
            },
        ];
    }

    private static async Task<ZzzFullAvatarData> LoadTestDataAsync(string filename)
    {
        string filePath = Path.Combine(TestDataPath, filename);
        string json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<ZzzFullAvatarData>(json);

        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
