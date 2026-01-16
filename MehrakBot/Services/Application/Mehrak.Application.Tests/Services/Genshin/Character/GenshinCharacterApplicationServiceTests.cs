#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Abstractions;
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
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.Character;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinCharacterApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin");

    [SetUp]
    public void Setup()
    {
        m_DbFactory = new TestDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        m_DbFactory.Dispose();
    }

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

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
    public async Task ExecuteAsync_UpdatesCharacterCache_WhenCharacterListFetched()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        characterCacheMock.Verify(x => x.UpsertCharacters(
                Game.Genshin,
                It.Is<IEnumerable<string>>(names => names.Count() == charList.Count && names.Contains("Traveler"))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CharacterNotFound_ReturnsNotFoundMessage()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var context = CreateContext(1, 1ul, "test", ("character", "NonExistentCharacter"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("NonExistentCharacter"));
        }

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.Genshin, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CharacterFoundByAlias_ReturnsSuccess()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, imageRepositoryMock,
            imageUpdaterMock, cardMock, gameRoleApiMock, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var aliases = new Dictionary<string, string> { { "MC", "Traveler" } };
        characterCacheMock
            .Setup(x => x.GetAliases(Game.Genshin))
            .Returns(aliases);

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        cardMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("character", "MC"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        characterCacheMock.Verify(x => x.UpsertCharacters(Game.Genshin, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CharacterDetailApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character data"));
        });

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.Genshin, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_WhenCharacterImageNotExists_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _, _, _) =
            SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        wikiApiMock
            .Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character Image"));
        }
    }

    [Test]
    public async Task ExecuteAsync_WikiApiReturnsEmptyImageUrl_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _, _, _) =
            SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
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

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

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
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, imageRepositoryMock, imageUpdaterMock, _, gameRoleApiMock, _, _, _) =
            SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, metricsMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
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

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.Genshin), "traveler"), Times.Once);

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.Genshin, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_WithWikiImageDownload_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false); // Force wiki download

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.Is<string>(x => x.StartsWith("genshin_weapon_"))))
            .ReturnsAsync(true);

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

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify wiki API was called
        wikiApiMock.Verify(x => x.GetAsync(It.IsAny<WikiApiContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenWeaponImagesMissing_FetchesFromWikiAndUpdates()
    {
        // Arrange
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        typeof(WeaponDetail).GetProperty("Type")!.SetValue(characterDetail.List[0].Weapon, 1);

        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string path, CancellationToken _) => !path.Contains("weapon"));

        var wikiResponse = JsonNode.Parse("""
                                          {
                                          "data": {
                                          "page": {
                                          "icon_url": "https://example.com/icon.png",
                                          "modules": [
                                          {
                                          "components": [
                                          {
                                          "component_id": "gallery_character",
                                          "data": "{\"list\": [{\"img\": \"https://example.com/1.png\"}, {\"img\": \"https://example.com/2.png\"}]}"
                                          }
                                          ]
                                          }
                                          ]
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

        imageUpdaterMock
            .Setup(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify wiki API was called
        wikiApiMock.Verify(x => x.GetAsync(It.IsAny<WikiApiContext>()), Times.AtLeastOnce);

        // Verify UpdateImageAsync called for base weapon icon
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
            It.Is<IImageData>(d => d.Url == characterDetail.List[0].Weapon.Icon),
            It.IsAny<IImageProcessor>()), Times.Once);

        // Verify UpdateMultiImageAsync called for ascended weapon icon
        imageUpdaterMock.Verify(x => x.UpdateMultiImageAsync(
            It.Is<IMultiImageData>(d => d.AdditionalUrls.Count() == 2 &&
                d.AdditionalUrls.Contains(characterDetail.List[0].Weapon.Icon) &&
                d.AdditionalUrls.Contains("https://example.com/2.png")),
            It.IsAny<IMultiImageProcessor>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, _, _, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
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

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        var charData = characterDetail.List[0];
        var expectedImageCount = charData.Constellations.Count + charData.Skills.Count + charData.Relics.Count + 1;

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.Exactly(expectedImageCount));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
            Assert.That(stored.Region, Is.EqualTo(Server.Asia.ToString()));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var seededProfile = SeedUserProfile(userContext, 1ul, 1, 1ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = seededProfile.Id,
            Game = Game.Genshin,
            Region = Server.Asia.ToString(),
            GameUid = profile.GameUid
        });
        await userContext.SaveChangesAsync();

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
        Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        SeedUserProfile(userContext, 1ul, 2, 99999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_WikiFallback_WhenCnFails_UsesAlternateLocale()
    {
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.Is<string>(x => x.StartsWith("genshin_weapon_"))))
            .ReturnsAsync(true);

        wikiApiMock
            .Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.CN)))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "CN down"));
        var enResponse = JsonNode.Parse("""
                                       {
                                         "data": { "page": { "header_img_url": "https://example.com/en_character.png" } }
                                       }
                                       """);
        wikiApiMock
            .Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.EN)))
            .ReturnsAsync(Result<JsonNode>.Success(enResponse!));
        wikiApiMock
            .Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale != WikiLocales.CN && c.Locale != WikiLocales.EN)))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "not used"));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        var context = CreateContext(7, 7ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        var result = await service.ExecuteAsync(context);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        wikiApiMock.Verify(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.CN)), Times.Once);
        wikiApiMock.Verify(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.EN)), Times.Once);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.Is<IImageData>(d => d.Url == "https://example.com/en_character.png"), It.IsAny<IImageProcessor>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WikiFallback_WhenCnReturnsEmpty_UsesAlternateLocale()
    {
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>("Aether_TestData.json");
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.Is<string>(x => x.StartsWith("genshin_weapon_"))))
            .ReturnsAsync(true);

        var cnEmpty = JsonNode.Parse("""
                                    { "data": { "page": { "header_img_url": "" } } }
                                    """);
        wikiApiMock
            .Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.CN)))
            .ReturnsAsync(Result<JsonNode>.Success(cnEmpty!));

        var enResponse = JsonNode.Parse("""
                                       { "data": { "page": { "header_img_url": "https://example.com/en_character.png" } } }
                                       """);
        wikiApiMock
            .Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.EN)))
            .ReturnsAsync(Result<JsonNode>.Success(enResponse!));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        var context = CreateContext(8, 8ul, "test", ("character", "Traveler"), ("server", Server.Asia.ToString()));

        var result = await service.ExecuteAsync(context);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        wikiApiMock.Verify(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.CN)), Times.Once);
        wikiApiMock.Verify(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.EN)), Times.Once);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.Is<IImageData>(d => d.Url == "https://example.com/en_character.png"), It.IsAny<IImageProcessor>()), Times.Once);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Aether_TestData.json", "Traveler")]
    [TestCase("Aether_WithSet_TestData.json", "Traveler")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, string characterName)
    {
        // Arrange
        var (service, characterApiMock, _, _, _, gameRoleApiMock, _, attachmentStorageMock, _) = SetupIntegrationTest();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var characterDetail = await LoadTestDataAsync<GenshinCharacterDetail>(testDataFile);
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(characterDetail));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test",
            ("character", characterName), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        }
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));

        var attachment = result.Data.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(!string.IsNullOrWhiteSpace(attachment!.FileName));

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment.FileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var characterName = "Traveler"; // Replace with a character you own

        using (Assert.EnterMultipleScope())
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        }

        var (service, storedAttachments, _) = SetupRealApiIntegrationTest();

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), testLtUid, testLToken,
             ("character", characterName), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True, $"API call failed: {result.ErrorMessage}");

        if (result.IsSuccess)
        {
            var attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
            Assert.That(storedAttachments.TryGetValue(attachment!.FileName, out var storedStream), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
                Assert.That(storedStream!.Length, Is.GreaterThan(0));
            }

            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, $"CharacterRealApi_{characterName}.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
        GenshinCharacterApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<GenshinCharacterInformation>> CardServiceMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<GenshinCharacterInformation>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail,
            GenshinCharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var metricsMock = new Mock<IMetricsService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinCharacterApplicationService>>();

        characterCacheMock
            .Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinCharacterApplicationService(
            cardServiceMock.Object,
            characterCacheMock.Object,
            characterApiMock.Object,
            wikiApiMock.Object,
            imageRepositoryMock.Object,
            imageUpdaterMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock,
            cardServiceMock, gameRoleApiMock, metricsMock, attachmentStorageMock, userContext);
    }

    private (
        GenshinCharacterApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var cardService = new GenshinCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData,
            GenshinCharacterDetail, GenshinCharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var metricsMock = new Mock<IMetricsService>();

        characterCacheMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinCharacterApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        cardService.InitializeAsync().Wait();

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinCharacterApplicationService(
            cardService,
            characterCacheMock.Object,
            characterApiMock.Object,
            wikiApiMock.Object,
            imageRepositoryMock.Object,
            imageUpdaterService,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, gameRoleApiMock,
            metricsMock, attachmentStorageMock, userContext);
    }

    private (GenshinCharacterApplicationService Service, Dictionary<string, MemoryStream> StoredAttachments, UserDbContext UserContext) SetupRealApiIntegrationTest()
    {
        var cardService = new GenshinCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharacterCardService>>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock.Setup(x => x.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<GenshinBasicCharacterData>?)null);

        var characterApiService = new GenshinCharacterApiService(
            cacheServiceMock.Object,
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinCharacterApiService>>());

        var characterCacheServiceMock = new Mock<ICharacterCacheService>();
        characterCacheServiceMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        var wikiApiService = new WikiApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<WikiApiService>>());

        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var metricsMock = new Mock<IMetricsService>();

        cardService.InitializeAsync().Wait();

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

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinCharacterApplicationService(
            cardService,
            characterCacheServiceMock.Object,
            characterApiService,
            wikiApiService,
            S3TestHelper.Instance.ImageRepository,
            imageUpdaterService,
            metricsMock.Object,
            gameRoleApiService,
            userContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<GenshinCharacterApplicationService>>());

        return (service, storedAttachments, userContext);
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
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<T>(json) ??
               throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    private static UserProfileModel SeedUserProfile(UserDbContext userContext, ulong userId, int profileId, ulong ltUid)
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

    private static IApplicationContext CreateContext(ulong userId, ulong ltUid, string lToken, params (string Key, object Value)[] parameters)
    {
        var mock = new Mock<IApplicationContext>();
        mock.Setup(x => x.UserId).Returns(userId);
        mock.SetupGet(x => x.LtUid).Returns(ltUid);
        mock.SetupGet(x => x.LToken).Returns(lToken);

        var paramDict = parameters.ToDictionary(k => k.Key, v => v.Value?.ToString());
        mock.Setup(x => x.GetParameter(It.IsAny<string>()))
            .Returns((string key) => paramDict.GetValueOrDefault(key));

        return mock.Object;
    }

    #endregion
}
