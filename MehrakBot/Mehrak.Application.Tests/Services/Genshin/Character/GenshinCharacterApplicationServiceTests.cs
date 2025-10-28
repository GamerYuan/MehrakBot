#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Genshin.Character;
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

namespace Mehrak.Application.Tests.Services.Genshin.Character;

[Parallelizable(ParallelScope.Self)]
public class GenshinCharacterApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "API Error"));

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "NonExistentCharacter"))
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

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        // Setup alias mapping
        var aliases = new Dictionary<string, string> { { "MC", "Traveler" } };
        characterCacheMock
            .Setup(x => x.GetAliases(Game.Genshin))
            .Returns(aliases);

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        cardMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "MC"))
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
    public async Task ExecuteAsync_CharacterDetailApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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
    public async Task ExecuteAsync_WikiApiError_WhenCharacterImageNotExists_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        wikiApiMock
            .Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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
    public async Task ExecuteAsync_WikiApiReturnsEmptyImageUrl_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var wikiResponse = JsonNode.Parse("""
                                          {
                                              "data": {
                                                  "page": {
                                                      "header_img_url": ""
                                                  }
                                              }
                                          }
                                          """);
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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
        var (service, characterApiMock, _, _, imageRepositoryMock, imageUpdaterMock, _, gameRoleApiMock, _) =
            SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true); // Character image exists, skip wiki

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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
        var (service, characterApiMock, _, _, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, metricsMock) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
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
        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.Genshin), "traveler"), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_WithWikiImageDownload_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false); // Force wiki download

        var wikiResponse = JsonNode.Parse("""
                                          {
                                              "data": {
                                                  "page": {
                                                      "header_img_url": "https://example.com/character.png"
                                                  }
                                              }
                                          }
                                          """);
        wikiApiMock
            .Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);

        // Verify wiki API was called
        wikiApiMock.Verify(x => x.GetAsync(It.IsAny<WikiApiContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, _, _, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinCharacterApplicationContext(
            1,
            ("character", "Traveler"))
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var charData = characterDetail.List[0];
        var expectedImageCount = 1 + charData.Constellations.Count + charData.Skills.Count + charData.Relics.Count;

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.Exactly(expectedImageCount));
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Aether_TestData.json", "Traveler")]
    [TestCase("Aether_WithSet_TestData.json", "Traveler")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, string characterName)
    {
        // Arrange
        var (service, characterApiMock, _, _, _, gameRoleApiMock, _) = SetupIntegrationTest();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>(testDataFile);
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        var context = new GenshinCharacterApplicationContext(
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
            $"CharacterIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

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
        string characterName = "Traveler"; // Replace with a character you own

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var service = SetupRealApiIntegrationTest();

        var context = new GenshinCharacterApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(),
            ("character", characterName))
        {
            LtUid = testLtUid,
            LToken = testLToken,
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
            string outputImagePath = Path.Combine(outputDirectory, $"CharacterRealApi_{characterName}.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        GenshinCharacterApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>>
        CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<GenshinCharacterInformation>> CardServiceMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<GenshinCharacterInformation>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail,
            CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var metricsMock = new Mock<IMetricsService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinCharacterApplicationService>>();

        // Setup default empty aliases
        characterCacheMock
            .Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        var service = new GenshinCharacterApplicationService(
            cardServiceMock.Object,
            characterCacheMock.Object,
            characterApiMock.Object,
            wikiApiMock.Object,
            imageRepositoryMock.Object,
            imageUpdaterMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock,
            cardServiceMock, gameRoleApiMock, metricsMock);
    }

    private static (
        GenshinCharacterApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>>
        CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new GenshinCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData,
            GenshinCharacterDetail, CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var metricsMock = new Mock<IMetricsService>();

        // Setup default empty aliases
        characterCacheMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns(new Dictionary<string, string>());

        // Mock image repository to always return true for FileExists (images
        // are in MongoDB)
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

        var loggerMock = new Mock<ILogger<GenshinCharacterApplicationService>>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new GenshinCharacterApplicationService(
            cardService,
            characterCacheMock.Object,
            characterApiMock.Object,
            wikiApiMock.Object,
            imageRepositoryMock.Object,
            imageUpdaterService,
            metricsMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, gameRoleApiMock,
            metricsMock);
    }

    private static GenshinCharacterApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new GenshinCharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Simple in-memory cache service for tests
        var cacheServiceMock = new Mock<ICacheService>();
        // Return null for all cache gets (no caching behavior in test)
        cacheServiceMock.Setup(x => x.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<GenshinBasicCharacterData>?)null);

        // Real character API service
        var characterApiService = new GenshinCharacterApiService(
            cacheServiceMock.Object,
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinCharacterApiService>>());

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

        var service = new GenshinCharacterApplicationService(
            cardService,
            characterCacheServiceMock.Object,
            characterApiService,
            wikiApiService,
            MongoTestHelper.Instance.ImageRepository,
            imageUpdaterService,
            metricsMock.Object,
            gameRoleApiService,
            Mock.Of<ILogger<GenshinCharacterApplicationService>>());

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

    private static List<GenshinBasicCharacterData> CreateTestCharacterList()
    {
        return
        [
            new GenshinBasicCharacterData
            {
                Id = 10000005,
                Icon = "",
                Name = "Traveler",
                Element = "Anemo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 0,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            }
        ];
    }

    private static async Task<T> LoadTestDataAsync<T>(string filename)
    {
        string filePath = Path.Combine(TestDataPath, filename);
        string json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<T>(json) ??
               throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}