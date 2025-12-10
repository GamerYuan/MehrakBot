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
        var (service, _, _, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "API Error"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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
            Assert.That(result.ErrorMessage, Does.Contain("Character data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_UpdatesCharacterCache_WhenCharacterListFetched()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

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
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, _, _, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = new HsrCharacterApplicationContext(1, ("character", "NonExistentCharacter"), ("server", Server.Asia.ToString()))
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
                Does.Contain("NonExistentCharacter"));
        });

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CharacterFoundByAlias_ReturnsSuccess()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, _, imageRepositoryMock,
            imageUpdaterMock, cardMock, gameRoleApiMock, _, _, _) = SetupMocks();

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

        var context = new HsrCharacterApplicationContext(1, ("character", "TB"), ("server", Server.Asia.ToString()))
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
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
        });

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_ForRelics_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _, _, _) =
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

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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
        });

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WikiApiError_ForLightCone_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, _, _, gameRoleApiMock, _, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        // Setup to return true for relics but false for light cone
        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .Returns((string fileName, CancellationToken _) => Task.FromResult(!fileName.Contains("21004"))); // Light cone ID

        // Wiki returns error for light cone
        wikiApiMock.Setup(x => x.GetAsync(It.Is<WikiApiContext>(c => c.Game == Game.HonkaiStarRail)))
            .ReturnsAsync(Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Wiki API Error"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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
            Assert.That(result.ErrorMessage, Does.Contain("Light Cone Data"));
        });

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock, _, gameRoleApiMock, _, _, _
                ) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true); // All images exist, skip wiki

        // Mock wiki to return valid data for relics
        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RelicImagesMissing_FetchesFromWikiAndAddsSetName()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, _, relicRepositoryMock) = SetupMocks();

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
                                    ""component_id"": ""gallery_character"",
                                    ""data"": ""{escapedRelicJson}""
                                }}
                            ]
                        }}
                    ]
                }}
            }}
        }}");

        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync((WikiApiContext c) =>
            {
                if (c.Locale == WikiLocales.EN)
                    return Result<JsonNode>.Success(wikiResponseEn!);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError, "Not Found");
            });

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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

            // Verify Set Name was added
            relicRepositoryMock.Verify(x => x.AddSetName(118, "Test Relic Set"), Times.Once);

            // Verify Image Update was called for the relic
            imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(img => img.Url == "https://example.com/relic_icon_1.png"),
                It.IsAny<IImageProcessor>()), Times.Once);
            imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(img => img.Url == "https://example.com/relic_icon_2.png"),
                It.IsAny<IImageProcessor>()), Times.Once);
            imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(img => img.Url == "https://example.com/relic_icon_3.png"),
                It.IsAny<IImageProcessor>()), Times.Once);
            imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(img => img.Url == "https://example.com/relic_icon_4.png"),
                It.IsAny<IImageProcessor>()), Times.Once);
        });

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, metricsMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock wiki to return valid data
        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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
        });

        // Verify metrics tracked
        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiStarRail), "trailblazer"),
            Times.Once);

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, _, _) = SetupMocks();

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

        var context = new HsrCharacterApplicationContext(1, ("character", "trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var character = charList.AvatarList.First(x => x.Name == "Trailblazer");
        var expectedImageCount = 1 + // Character portrait
                                 character.Relics.Count +
                                 character.Ornaments.Count + // Relics
                                 character.Skills.Count + // Skills
                                 character.Ranks.Count; // Eidolons

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeast(expectedImageCount));

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, userRepositoryMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // User exists with matching profile but no stored GameUids
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

        // Force early exit after UpdateGameUid by making char API fail
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        userRepositoryMock.Verify(
            x => x.CreateOrUpdateUserAsync(It.Is<UserDto>(u =>
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
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, userRepositoryMock, _) = SetupMocks();

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
                                Game.HonkaiStarRail,
                                new Dictionary<string, string> { { Server.Asia.ToString(), profile.GameUid } }
                            }
                        }
                    }
                ]
            });

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
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
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _, userRepositoryMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // Case: user not found
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserDto?)null);

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);

        // Case: user exists but no matching profile
        userRepositoryMock.Reset();
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync(new UserDto
            {
                Id = 1ul,
                Profiles =
                [
                    new() { LtUid = 99999ul, LToken = "test" }
                ]
            });

        await service.ExecuteAsync(context);
        userRepositoryMock.Verify(x => x.CreateOrUpdateUserAsync(It.IsAny<UserDto>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RelicWiki_FallbackLocale_UsesAlternateLocaleWhenENMissingModule()
    {
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, _, relicRepositoryMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        // Force relic set missing images (setId 118 expected file names hsr_1181..hsr_1184)
        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name, CancellationToken _) => !name.Contains("118"));

        // EN locale returns page without Set module
        var enResponse = JsonNode.Parse("{\"data\":{\"page\":{\"name\":\"Missing Set Module\",\"modules\":[]}}}");
        // CN locale returns Set module with list
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
        var cnResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"CN Relic Set\",\"modules\":[{{\"components\":[{{\"component_id\":\"gallery_character\",\"data\":\"{escapedRelicJson}\"}}]}}]}}}}}}");

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

        var context = new HsrCharacterApplicationContext(1, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        var result = await service.ExecuteAsync(context);

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        // CN provided list, images updated
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.Is<IImageData>(d => d.Url.Contains("relic_cn_1")), It.IsAny<IImageProcessor>()), Times.Once);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.Is<IImageData>(d => d.Url.Contains("relic_cn_4")), It.IsAny<IImageProcessor>()), Times.Once);
        // EN set name not added (since module missing)
        relicRepositoryMock.Verify(x => x.AddSetName(It.Is<int>(x => x == 118), It.Is<string>(x => x.Equals("Missing Set Module"))), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RelicWiki_ENSetName_StopsLocaleIteration()
    {
        var (service, characterApiMock, _, wikiApiMock, imageRepositoryMock, imageUpdaterMock, cardServiceMock,
            gameRoleApiMock, _, _, relicRepositoryMock) = SetupMocks();

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
        var enResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"EN Relic Set\",\"modules\":[{{\"components\":[{{\"component_id\":\"gallery_character\",\"data\":\"{escapedRelicJson}\"}}]}}]}}}}}}");

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

        var context = new HsrCharacterApplicationContext(2, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 2ul,
            LToken = "test"
        };

        var result = await service.ExecuteAsync(context);
        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);

        // Locale iteration should stop at EN for each needed set (no other locales)
        Assert.That(calledLocales.All(l => l == WikiLocales.EN), Is.True, $"Unexpected locales: {string.Join(',', calledLocales)}");
        relicRepositoryMock.Verify(x => x.AddSetName(118, "EN Relic Set"), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RelicWiki_PartialThenCompleteJson_CompletesMissingPieces()
    {
        var (service1, characterApiMock1, _, wikiApiMock1, imageRepositoryMock1, imageUpdaterMock1, cardServiceMock1,
            gameRoleApiMock1, _, _, _) = SetupMocks();

        gameRoleApiMock1.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock1.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        // Simulate repository state for file existence
        HashSet<string> existingFiles = [];
        imageRepositoryMock1.Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string name, CancellationToken _) => existingFiles.Contains(name));
        imageRepositoryMock1.Setup(x => x.FileExistsAsync(It.Is<string>(x => x.Contains("21004"))))
            .ReturnsAsync(true); // Light cone ID
        imageUpdaterMock1.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .Returns((IImageData data, IImageProcessor _) =>
            {
                existingFiles.Add(data.Name);
                return Task.FromResult(true);
            });
        cardServiceMock1.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrCharacterInformation>>()))
            .ReturnsAsync(new MemoryStream());

        // First run returns partial list (2 icons)
        var partialJson = JsonSerializer.Serialize(new
        {
            list = new[]
            {
                new { icon_url = "https://example.com/relic_partial_1.png" },
                new { icon_url = "https://example.com/relic_partial_2.png" }
            }
        });
        var escapedPartial = partialJson.Replace("\"", "\\\"");
        var partialResponse = JsonNode.Parse($"{{\"data\":{{\"page\":{{\"name\":\"Partial Relic Set\",\"modules\":[{{\"components\":[{{\"component_id\":\"gallery_character\",\"data\":\"{escapedPartial}\"}}]}}]}}}}}}");
        wikiApiMock1.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(partialResponse!));

        var context1 = new HsrCharacterApplicationContext(3, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 3ul,
            LToken = "test"
        };

        var firstResult = await service1.ExecuteAsync(context1);
        Assert.That(firstResult.IsSuccess, Is.True, firstResult.ErrorMessage);
        // After first run only two images should exist
        Assert.That(existingFiles.Count(f => f.StartsWith("hsr_118")), Is.EqualTo(2));

        // Second run with complete list
        var (service2, characterApiMock2, _, wikiApiMock2, imageRepositoryMock2, imageUpdaterMock2, cardServiceMock2,
            gameRoleApiMock2, _, _, _) = SetupMocks();

        gameRoleApiMock2.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        characterApiMock2.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));
        // Inject existingFiles state into second set of mocks
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
            .ReturnsAsync(true); // Light cone ID

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

        var context2 = new HsrCharacterApplicationContext(3, ("character", "Trailblazer"), ("server", Server.Asia.ToString()))
        {
            LtUid = 3ul,
            LToken = "test"
        };
        var secondResult = await service2.ExecuteAsync(context2);
        Assert.That(secondResult.IsSuccess, Is.True, secondResult.ErrorMessage);

        // All four relic piece images should now exist (hsr_1181..hsr_1184)
        Assert.That(existingFiles.Count(f => f.StartsWith("hsr_118")), Is.EqualTo(4));
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("CharList_TestData_1.json", "Trailblazer")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, string characterName)
    {
        // Arrange
        var (service, characterApiMock, _, _, _, gameRoleApiMock, _, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = new HsrCharacterApplicationContext(
            DbTestHelper.Instance.GetUniqueUserId(),
            ("character", characterName), ("server", Server.Asia.ToString()))
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
        Assert.That(attachment!.Content.Length, Is.GreaterThan(0), "Expected a non-empty card image");

        // Save the generated card for manual inspection
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(
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

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];
        var characterName = "Trailblazer"; // Replace with a character you own

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var service = SetupRealApiIntegrationTest();

        var context = new HsrCharacterApplicationContext(
            DbTestHelper.Instance.GetUniqueUserId(),
            ("character", characterName), ("server", Server.Asia.ToString()))
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
            Assert.That(attachment!.Content.Length, Is.GreaterThan(0));

            // Save output
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, $"HsrCharacterRealApi_{characterName}.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        HsrCharacterApplicationService Service,
        Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock
        ,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<HsrCharacterInformation>> CardServiceMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock,
        Mock<IUserRepository> UserRepositoryMock,
        Mock<IRelicRepository> RelicRepositoryMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrCharacterInformation>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock =
            new Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var metricsMock = new Mock<IMetricsService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var relicRepositoryMock = new Mock<IRelicRepository>();
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
            userRepositoryMock.Object,
            relicRepositoryMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, imageUpdaterMock,
            cardServiceMock, gameRoleApiMock, metricsMock, userRepositoryMock, relicRepositoryMock);
    }

    private static (
        HsrCharacterApplicationService Service,
        Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock
        ,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IMetricsService> MetricsMock,
        Mock<IUserRepository> UserRepositoryMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var relicRepositoryMock = new Mock<IRelicRepository>();
        relicRepositoryMock.Setup(x => x.GetSetName(It.IsAny<int>()))
            .ReturnsAsync((int setId) => $"Relic Set {setId}");

        var cardService = new HsrCharacterCardService(
            DbTestHelper.Instance.ImageRepository,
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
        var wikiResponse =
            JsonNode.Parse("{\"data\":{\"page\":{\"icon_url\":\"https://example.com/icon.png\",\"modules\":[]}}}");
        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            DbTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<HsrCharacterApplicationService>>();
        var userRepositoryMock = new Mock<IUserRepository>();

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
            userRepositoryMock.Object,
            relicRepositoryMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, wikiApiMock, imageRepositoryMock, gameRoleApiMock,
            metricsMock, userRepositoryMock);
    }

    private static HsrCharacterApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var relicRepositoryMock = new Mock<IRelicRepository>();
        relicRepositoryMock.Setup(x => x.GetSetName(It.IsAny<int>()))
            .ReturnsAsync((int setId) => $"Relic Set {setId}");

        var cardService = new HsrCharacterCardService(
            DbTestHelper.Instance.ImageRepository,
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
            DbTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        // Mock metrics (don't want to send real metrics in tests)
        var metricsMock = new Mock<IMetricsService>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var userRepositoryMock = new Mock<IUserRepository>();

        var service = new HsrCharacterApplicationService(
            cardService,
            wikiApiService,
            imageUpdaterService,
            DbTestHelper.Instance.ImageRepository,
            characterCacheServiceMock.Object,
            characterApiService,
            metricsMock.Object,
            gameRoleApiService,
            userRepositoryMock.Object,
            relicRepositoryMock.Object,
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

    #endregion
}
