#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Abstractions;
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
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.Character;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrCharacterApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory1 = null!;
    private TestDbContextFactory m_DbFactory2 = null!;
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    [SetUp]
    public void Setup()
    {
        m_DbFactory1 = new TestDbContextFactory();
        m_DbFactory2 = new TestDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        m_DbFactory1.Dispose();
        m_DbFactory2.Dispose();
    }

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        var (service, _, _, _, _, _, _, _, gameRoleApiMock, _, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

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
        var (service, characterApiMock, _, _, _, _, _, _, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character data"));
        }
    }

    [Test]
    public async Task ExecuteAsync_UpdatesCharacterCache_WhenCharacterListFetched()
    {
        var (service, characterApiMock, characterCacheMock, _, _, _, _, _, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        characterCacheMock.Verify(x => x.UpsertCharacters(
                Game.HonkaiStarRail,
                It.Is<IEnumerable<string>>(names => names.Contains("Trailblazer"))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CharacterNotFound_ReturnsNotFoundMessage()
    {
        var (service, characterApiMock, characterCacheMock, _, _, _, _, _, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

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

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CharacterFoundByAlias_ReturnsSuccess()
    {
        var (service, characterApiMock, characterCacheMock, aliasServiceMock, _, imageRepositoryMock,
            imageUpdaterMock, cardMock, gameRoleApiMock, _, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var aliases = new Dictionary<string, string> { { "TB", "Trailblazer" } };
        aliasServiceMock.Setup(x => x.GetAliases(Game.HonkaiStarRail))
            .Returns(aliases);

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        cardMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("character", "TB"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
        }

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_ForRelics_ReturnsApiError()
    {
        var (service, characterApiMock, characterCacheMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _, relicContext, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .Returns((string fileName, CancellationToken _) => Task.FromResult(fileName.Contains("21004")));

        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
        }

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
        Assert.That(await relicContext.HsrRelics.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_ForLightCone_ReturnsApiError()
    {
        var (service, characterApiMock, characterCacheMock, _, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _, relicContext, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .Returns((string fileName, CancellationToken _) => Task.FromResult(!fileName.Contains("21004")));

        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Game == Game.HonkaiStarRail)))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Light Cone Data"));
        }

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
        Assert.That(await relicContext.HsrRelics.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        var (service, characterApiMock, characterCacheMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, _, gameRoleApiMock, _, _, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        }

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RelicImagesMissing_FetchesFromWikiAndAddsSetName()
    {
        var (service, characterApiMock, characterCacheMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, relicContext, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.Is<string>(x => x.Contains("1181")))).ReturnsAsync(false);

        var charList = await LoadTestDataAsync();

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var relicJson = JsonSerializer.Serialize(new
        {
            list = new List<object> {
                new
                {
                    icon_url = "https://example.com/relic_icon_1.png",
                },
                new
                {
                    icon_url = "https://example.com/relic_icon_2.png",
                },
                new
                {
                    icon_url = "https://example.com/relic_icon_3.png",
                },
                new
                {
                    icon_url = "https://example.com/relic_icon_4.png",
                }
            }
        });

        // Escape quotes for JSON string inside JSON
        var escapedRelicJson = relicJson.Replace("\"", "\\\"");

        var wikiResponseEn = JsonNode.Parse($@"
        {{
            ""data"": {{
                ""page"": {{
                    ""name"": ""Test Relic Set"",
                    ""modules"": [
                        {{
                            ""components"": [
                                {{
                                    ""component_id"": ""set"",
                                    ""data"": ""{escapedRelicJson}""
                                }}
                            ]
                        }}
                    ]
                }}
            }}
        }}");

        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync((WikiApiContext c) => Result<JsonNode>.Success(wikiResponseEn!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(await relicContext.HsrRelics.AnyAsync(x => x.SetId == 118 && x.SetName == "Test Relic Set"), Is.True);
        }

        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
            It.Is<IImageData>(img => img.Url == "https://example.com/relic_icon_1.png"),
            It.IsAny<IImageProcessor>()), Times.Once);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
            It.Is<IImageData>(img => img.Url == "https://example.com/relic_icon_4.png"),
            It.IsAny<IImageProcessor>()), Times.Once);

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        var (service, characterApiMock, characterCacheMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, metricsMock, _, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        });
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
        Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);

        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiStarRail), "trailblazer"),
            Times.Once);

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        var (service, characterApiMock, characterCacheMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Api Error"));

        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(x => x.EntryPage.Equals("48"))))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("character", "trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var character = charList.AvatarList.First(x => x.Name == "Trailblazer");
        var expectedImageCount = 1 +
                                 character.Relics.Count +
                                 character.Ornaments.Count +
                                 character.Skills.Count +
                                 character.Ranks.Count;

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeast(expectedImageCount));

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        var (service, characterApiMock, _, _, _, _, _, _, gameRoleApiMock, _, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
        Assert.That(stored.Region, Is.EqualTo(Server.Asia.ToString()));
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        var (service, characterApiMock, _, _, _, _, _, _, gameRoleApiMock, _, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var seededProfile = SeedUserProfile(userContext, 1ul, 1, 1ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = seededProfile.Id,
            Game = Game.HonkaiStarRail,
            Region = Server.Asia.ToString(),
            GameUid = profile.GameUid
        });
        await userContext.SaveChangesAsync();

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
        Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        var (service, characterApiMock, _, _, _, _, _, _, gameRoleApiMock, _, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);

        SeedUserProfile(userContext, 1ul, 2, 99999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RelicWiki_FallbackLocale_UsesAlternateLocaleWhenENMissingModule()
    {
        var (service, characterApiMock, _, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, relicContext, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name, CancellationToken _) => !name.Contains("118"));

        var enResponse = JsonNode.Parse("{\"data\":{\"page\":{\"name\":\"Missing Set Module\",\"modules\":[]}}}");
        var relicJson = JsonSerializer.Serialize(new
        {
            list = new[]
            {
                new { icon_url = "https://example.com/relic_cn_1.png" },
                new { icon_url = "https://example.com/relic_cn_2.png" },
                new { icon_url = "https://example.com/relic_cn_3.png" },
                new { icon_url = "https://example.com/relic_cn_4.png" }
            }
        });
        var escapedRelicJson = relicJson.Replace("\"", "\\\"");
        var cnResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"CN Relic Set\",\"modules\":[{{\"components\":[{{\"component_id\":\"set\",\"data\":\"{escapedRelicJson}\"}}]}}]}}}}}}");

        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.EN)))
            .ReturnsAsync(Result<JsonNode>.Success(enResponse!));
        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale == WikiLocales.CN)))
            .ReturnsAsync(Result<JsonNode>.Success(cnResponse!));
        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Locale != WikiLocales.EN && c.Locale != WikiLocales.CN)))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Not Found"));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        var context = CreateContext(1, 1ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        var result = await service.ExecuteAsync(context);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(await relicContext.HsrRelics.AnyAsync(x => x.SetId == 118 && x.SetName == "Missing Set Module"), Is.True);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.Is<IImageData>(d => d.Url.Contains("relic_cn_1")), It.IsAny<IImageProcessor>()), Times.Once);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.Is<IImageData>(d => d.Url.Contains("relic_cn_4")), It.IsAny<IImageProcessor>()), Times.Once);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RelicWiki_ENSetName_StopsLocaleIteration()
    {
        var (service, characterApiMock, _, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, relicContext, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name, CancellationToken _) => !name.Contains("118"));

        var relicJson = JsonSerializer.Serialize(new
        {
            list = new[]
            {
                new { icon_url = "https://example.com/relic_en_1.png" },
                new { icon_url = "https://example.com/relic_en_2.png" },
                new { icon_url = "https://example.com/relic_en_3.png" },
                new { icon_url = "https://example.com/relic_en_4.png" }
            }
        });
        var escapedRelicJson = relicJson.Replace("\"", "\\\"");
        var enResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"EN Relic Set\",\"modules\":[{{\"components\":[{{\"component_id\":\"set\",\"data\":\"{escapedRelicJson}\"}}]}}]}}}}}}");

        List<WikiLocales> calledLocales = [];
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync((WikiApiContext c) =>
            {
                calledLocales.Add(c.Locale);
                if (c.Locale == WikiLocales.EN)
                    return Result<JsonNode>.Success(enResponse!);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Not Needed");
            });

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        var context = CreateContext(2, 2ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        var result = await service.ExecuteAsync(context);
        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);

        Assert.That(calledLocales.All(l => l == WikiLocales.EN), Is.True, $"Unexpected locales: {string.Join(',', calledLocales)}");
        Assert.That(await relicContext.HsrRelics.AnyAsync(x => x.SetId == 118 && x.SetName == "EN Relic Set"), Is.True);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RelicWiki_PartialThenCompleteJson_CompletesMissingPieces()
    {
        var (service1, characterApiMock1, _, _, wikiApiMock1, imageRepositoryMock1, imageUpdaterMock1, cardServiceMock1,
            gameRoleApiMock1, _, _, attachmentStorageMock1, _) = SetupMocks();

        gameRoleApiMock1.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock1.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        HashSet<string> existingFiles = [];
        imageRepositoryMock1.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name, CancellationToken _) => existingFiles.Contains(name));
        imageRepositoryMock1.Setup(x => x.FileExistsAsync(It.Is<string>(x => x.Contains("21004"))))
            .ReturnsAsync(true);
        imageUpdaterMock1.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .Returns((IImageData data, IImageProcessor _) =>
            {
                existingFiles.Add(data.Name);
                return Task.FromResult(true);
            });
        cardServiceMock1.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        var partialJson = JsonSerializer.Serialize(new
        {
            list = new[]
            {
                new { icon_url = "https://example.com/relic_partial_1.png" },
                new { icon_url = "https://example.com/relic_partial_2.png" }
            }
        });
        var escapedPartial = partialJson.Replace("\"", "\\\"");
        var partialResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"Partial Relic Set\",\"modules\":[{{\"components\":[{{\"component_id\":\"set\",\"data\":\"{escapedPartial}\"}}]}}]}}}}}}");
        wikiApiMock1.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(partialResponse!));

        var context1 = CreateContext(3, 3ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        var firstResult = await service1.ExecuteAsync(context1);
        Assert.That(firstResult.IsSuccess, Is.True, firstResult.ErrorMessage);
        Assert.That(existingFiles.Count(f => f.StartsWith("hsr_118")), Is.EqualTo(2));

        var (service2, characterApiMock2, _, _, wikiApiMock2, imageRepositoryMock2, imageUpdaterMock2, cardServiceMock2,
            gameRoleApiMock2, _, _, attachmentStorageMock2, _) = SetupMocks();

        gameRoleApiMock2.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        characterApiMock2.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock2.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name, CancellationToken _) => existingFiles.Contains(name));
        imageUpdaterMock2.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .Returns((IImageData data, IImageProcessor _) =>
            {
                existingFiles.Add(data.Name);
                return Task.FromResult(true);
            });
        cardServiceMock2.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        imageRepositoryMock2.Setup(x => x.FileExistsAsync(It.Is<string>(x => x.Contains("21004"))))
            .ReturnsAsync(true);

        var fullJson = JsonSerializer.Serialize(new
        {
            list = new[]
            {
                new { icon_url = "https://example.com/relic_full_1.png" },
                new { icon_url = "https://example.com/relic_full_2.png" },
                new { icon_url = "https://example.com/relic_full_3.png" },
                new { icon_url = "https://example.com/relic_full_4.png" }
            }
        });
        var escapedFull = fullJson.Replace("\"", "\\\"");
        var fullResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"Full Relic Set\",\"modules\":[{{\"name\":\"Set\",\"components\":[{{\"data\":\"{escapedFull}\"}}]}}]}}}}}}");
        wikiApiMock2.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(fullResponse!));

        var context2 = CreateContext(3, 3ul, "test", ("character", "Trailblazer"), ("server", Server.Asia.ToString()));

        var secondResult = await service2.ExecuteAsync(context2);
        Assert.That(secondResult.IsSuccess, Is.True, secondResult.ErrorMessage);

        Assert.That(existingFiles.Count(f => f.StartsWith("hsr_118")), Is.EqualTo(4));
        attachmentStorageMock1.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        attachmentStorageMock2.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("CharList_TestData_1.json", "Trailblazer")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, string characterName)
    {
        var (service, characterApiMock, _, _, _, _, gameRoleApiMock, _, attachmentStorageMock, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test",
            ("character", characterName), ("server", Server.Asia.ToString()));

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
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];
        var characterName = "Trailblazer";

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var (service, _, storedAttachments, _) = SetupRealApiIntegrationTest();

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), testLtUid, testLToken!,
            ("character", characterName), ("server", Server.Asia.ToString()));

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
            var outputImagePath = Path.Combine(outputDirectory, $"HsrCharacterRealApi_{characterName}.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
         HsrCharacterApplicationService Service,
         Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock,
         Mock<ICharacterCacheService> CharacterCacheMock,
         Mock<IAliasService> AliasServiceMock,
         Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
         Mock<IImageRepository> ImageRepositoryMock,
         Mock<IImageUpdaterService> ImageUpdaterMock,
         Mock<ICardService<HsrCharacterInformation>> CardServiceMock,
         Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
         Mock<IApplicationMetrics> MetricsMock,
         RelicDbContext RelicContext,
         Mock<IAttachmentStorageService> AttachmentStorageMock,
         UserDbContext UserContext
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrCharacterInformation>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var aliasServiceMock = new Mock<IAliasService>();
        var characterApiMock =
            new Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var metricsMock = new Mock<IApplicationMetrics>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrCharacterApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        aliasServiceMock.Setup(x => x.GetAliases(It.IsAny<Game>())).Returns([]);

        var userContext = m_DbFactory1.CreateDbContext<UserDbContext>();
        var relicContext = m_DbFactory2.CreateDbContext<RelicDbContext>();

        var service = new HsrCharacterApplicationService(
            cardServiceMock.Object,
            wikiApiMock.Object,
            imageUpdaterMock.Object,
            imageRepositoryMock.Object,
            characterCacheMock.Object,
            aliasServiceMock.Object,
            characterApiMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userContext,
            relicContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, aliasServiceMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock,
            cardServiceMock, gameRoleApiMock, metricsMock, relicContext, attachmentStorageMock, userContext);
    }

    private (
         HsrCharacterApplicationService Service,
         Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock,
         Mock<ICharacterCacheService> CharacterCacheMock,
         Mock<IAliasService> AliasServiceMock,
         Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
         Mock<IImageRepository> ImageRepositoryMock,
         Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
         Mock<IApplicationMetrics> MetricsMock,
         Mock<IAttachmentStorageService> AttachmentStorageMock,
         UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var userContext = m_DbFactory1.CreateDbContext<UserDbContext>();
        var relicContext = m_DbFactory2.CreateDbContext<RelicDbContext>();

        var cardService = new HsrCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            CreateRelicScopeFactory(relicContext),
            Mock.Of<ILogger<HsrCharacterCardService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        var aliasServiceMock = new Mock<IAliasService>();
        var characterApiMock = new Mock<ICharacterApiService<HsrBasicCharacterData,
            HsrCharacterInformation, CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var metricsMock = new Mock<IApplicationMetrics>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        aliasServiceMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
            .Returns([]);

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<HsrCharacterApplicationService>>();

        cardService.InitializeAsync().Wait();

        var service = new HsrCharacterApplicationService(
            cardService,
            wikiApiMock.Object,
            imageUpdaterService,
            imageRepositoryMock.Object,
            characterCacheMock.Object,
            aliasServiceMock.Object,
            characterApiMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userContext,
            relicContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, aliasServiceMock, wikiApiMock, imageRepositoryMock, gameRoleApiMock,
            metricsMock, attachmentStorageMock, userContext);
    }

    private (
        HsrCharacterApplicationService Service,
        IAttachmentStorageService AttachmentStorageService,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupRealApiIntegrationTest()
    {

        var userContext = m_DbFactory1.CreateDbContext<UserDbContext>();
        var relicContext = m_DbFactory2.CreateDbContext<RelicDbContext>();

        var cardService = new HsrCharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            CreateRelicScopeFactory(relicContext),
            Mock.Of<ILogger<HsrCharacterCardService>>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock.Setup(x => x.GetAsync<IEnumerable<HsrBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<HsrBasicCharacterData>?)null);

        var characterApiService = new HsrCharacterApiService(
            httpClientFactory.Object,
            cacheServiceMock.Object,
            Mock.Of<ILogger<HsrCharacterApiService>>());

        var characterCacheServiceMock = new Mock<ICharacterCacheService>();

        var aliasServiceMock = new Mock<IAliasService>();
        aliasServiceMock.Setup(x => x.GetAliases(It.IsAny<Game>()))
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

        var metricsMock = new Mock<IApplicationMetrics>();

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

        var service = new HsrCharacterApplicationService(
            cardService,
            wikiApiService,
            imageUpdaterService,
            S3TestHelper.Instance.ImageRepository,
            characterCacheServiceMock.Object,
            aliasServiceMock.Object,
            characterApiService,
            metricsMock.Object,
            gameRoleApiService,
            userContext,
            relicContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<HsrCharacterApplicationService>>());

        return (service, attachmentStorageMock.Object, storedAttachments, userContext);
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
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<HsrCharacterInformation>(json);

        if (result == null) throw new InvalidOperationException($"Failed to deserialize {filename}");

        var charData = new HsrBasicCharacterData
        {
            AvatarList = [result],
            EquipWiki = new Dictionary<string, string>
            {
                { "21004", "https://wiki.hoyolab.com/pc/hsr/entry/48" }
            },
            RelicWiki = new Dictionary<string, string>
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

    private static IServiceScopeFactory CreateRelicScopeFactory(RelicDbContext relicContext)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(RelicDbContext))).Returns(relicContext);

        var scope = new Mock<IServiceScope>();
        scope.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

        serviceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
        return scopeFactory.Object;
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

    #endregion
}
