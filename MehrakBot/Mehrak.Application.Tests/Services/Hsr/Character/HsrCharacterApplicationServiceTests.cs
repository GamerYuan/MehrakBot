#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Hsr.Character;
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

namespace Mehrak.Application.Tests.Services.Hsr.Character;

[Parallelizable(ParallelScope.Self)]
public class HsrCharacterApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"))
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
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
            "API Error"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"))
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
               Assert.That(result.ErrorMessage, Does.Contain("Character data"));
           });
    }

    [Test]
    public async Task ExecuteAsync_CharacterNotFound_ReturnsNotFoundMessage()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = new HsrCharacterApplicationContext(1, ("character", "NonExistentCharacter"))
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
        var (service, characterApiMock, characterCacheMock, _, imageRepositoryMock,
            imageUpdaterMock, cardMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        // Setup alias mapping
        var aliases = new Dictionary<string, string> { { "TB", "Trailblazer" } };
        characterCacheMock.Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        cardMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = new HsrCharacterApplicationContext(1, ("character", "TB"))
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
    public async Task ExecuteAsync_WikiApiError_ForRelics_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .Returns((string fileName) => Task.FromResult(fileName.Contains("21004")));

        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"))
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
        });
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_ForLightCone_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        // Setup to return true for relics but false for light cone
        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .Returns((string fileName) => Task.FromResult(!fileName.Contains("21004"))); // Light cone ID

        // Wiki returns error for light cone
        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Game == Game.HonkaiStarRail)))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"))
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
            Assert.That(result.ErrorMessage, Does.Contain("Light Cone Data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsBotError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, _, gameRoleApiMock, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true); // All images exist, skip wiki

        // Mock wiki to return valid data for relics
        var wikiResponse = JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"))
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
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, metricsMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock wiki to return valid data
        var wikiResponse = JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"))
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
        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiStarRail), "trailblazer"),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var wikiResponse = JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Api Error"));

        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(x => x.EntryPage.Equals("48"))))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrCharacterApplicationContext(1, ("character", "trailblazer"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var character = charList.AvatarList.First(x => x.Name == "Trailblazer");
        var expectedImageCount = 1 + // Character portrait
            (character.Relics.Count +
            character.Ornaments.Count) + // Relics
            character.Skills.Count + // Skills
            character.Ranks.Count; // Eidolons

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeast(expectedImageCount));
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("CharList_TestData_1.json", "Trailblazer")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, string characterName)
    {
        // Arrange
        var (service, characterApiMock, _, _, _, gameRoleApiMock, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = new HsrCharacterApplicationContext(
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
            $"HsrCharacterIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}_{characterName}.jpg");

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
        string characterName = "Trailblazer"; // Replace with a character you own

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var service = SetupRealApiIntegrationTest();

        var context = new HsrCharacterApplicationContext(
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
            string outputImagePath = Path.Combine(outputDirectory, $"HsrCharacterRealApi_{characterName}.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        HsrCharacterApplicationService Service,
        Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<HsrCharacterInformation>> CardServiceMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrCharacterInformation>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var metricsMock = new Mock<IMetricsService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<HsrCharacterApplicationService>>();

        // Setup default empty aliases
        characterCacheMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        var service = new HsrCharacterApplicationService(
            cardServiceMock.Object,
            wikiApiMock.Object,
            imageUpdaterMock.Object,
            imageRepositoryMock.Object,
            characterCacheMock.Object,
            characterApiMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock,
            cardServiceMock, gameRoleApiMock, metricsMock);
    }

    private static (
        HsrCharacterApplicationService Service,
        Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var relicRepositoryMock = new Mock<IRelicRepository>();
        relicRepositoryMock.Setup(x => x.GetSetName(It.IsAny<int>()))
            .ReturnsAsync((int setId) => $"Relic Set {setId}");

        var cardService = new HsrCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            relicRepositoryMock.Object,
            Mock.Of<ILogger<HsrCharacterCardService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<HsrBasicCharacterData,
           HsrCharacterInformation, CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var metricsMock = new Mock<IMetricsService>();

        // Setup default empty aliases
        characterCacheMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        // Mock image repository to always return true for FileExists (images are in MongoDB)
        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock wiki to return valid data
        var wikiResponse = JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<HsrCharacterApplicationService>>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new HsrCharacterApplicationService(
            cardService,
            wikiApiMock.Object,
            imageUpdaterService,
            imageRepositoryMock.Object,
            characterCacheMock.Object,
            characterApiMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, gameRoleApiMock,
            metricsMock);
    }

    private static HsrCharacterApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var relicRepositoryMock = new Mock<IRelicRepository>();
        relicRepositoryMock.Setup(x => x.GetSetName(It.IsAny<int>()))
            .ReturnsAsync((int setId) => $"Relic Set {setId}");

        var cardService = new HsrCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            relicRepositoryMock.Object,
            Mock.Of<ILogger<HsrCharacterCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Simple in-memory cache service for tests
        var cacheServiceMock = new Mock<ICacheService>();
        // Return null for all cache gets (no caching behavior in test)
        cacheServiceMock.Setup(x => x.GetAsync<IEnumerable<HsrBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<HsrBasicCharacterData>?)null);

        // Real character API service
        var characterApiService = new HsrCharacterApiService(
            httpClientFactory.Object,
            cacheServiceMock.Object,
            Mock.Of<ILogger<HsrCharacterApiService>>());

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

        var service = new HsrCharacterApplicationService(
            cardService,
            wikiApiService,
            imageUpdaterService,
            MongoTestHelper.Instance.ImageRepository,
            characterCacheServiceMock.Object,
            characterApiService,
            metricsMock.Object,
            gameRoleApiService,
            Mock.Of<ILogger<HsrCharacterApplicationService>>());

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

    private static async Task<HsrBasicCharacterData> LoadTestDataAsync(string filename = "Stelle_TestData.json")
    {
        string filePath = Path.Combine(TestDataPath, filename);
        string json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<HsrCharacterInformation>(json);

        if (result == null) throw new InvalidOperationException($"Failed to deserialize {filename}");

        var charData = new HsrBasicCharacterData
        {
            AvatarList = [result],
            EquipWiki = new()
            {
                { "21004", "https://wiki.hoyolab.com/pc/hsr/entry/48" }
            },
            RelicWiki = new()
            {
                { "61181", "https://wiki.hoyolab.com/pc/hsr/entry/1926" },
                { "61182", "https://wiki.hoyolab.com/pc/hsr/entry/1926" },
                { "61183", "https://wiki.hoyolab.com/pc/hsr/entry/1926" },
                { "61184", "https://wiki.hoyolab.com/pc/hsr/entry/1926" },
                { "63075", "https://wiki.hoyolab.com/pc/hsr/entry/143" },
                { "63076", "https://wiki.hoyolab.com/pc/hsr/entry/143" }
            }
        };

        return charData;
    }

    #endregion
}
