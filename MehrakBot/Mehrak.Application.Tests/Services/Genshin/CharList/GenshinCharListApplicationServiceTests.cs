#region

using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Services.Genshin.CharList;
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

namespace Mehrak.Application.Tests.Services.Genshin.CharList;

[Parallelizable(ParallelScope.Self)]
public class GenshinCharListApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "API Error"));

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, _, _, _, imageRepoMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync<CharacterListData>("CharList_TestData_1.json");
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

        imageRepoMock.Setup(x => x.FileExistsAsync(It.IsAny<string>())).ReturnsAsync((string val) => val.Contains("weapon"));

        // Make image update fail
        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, characterCacheMock, imageRepoMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync<CharacterListData>("CharList_TestData_1.json");
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

        imageRepoMock.Setup(x => x.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<GenshinBasicCharacterData>>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.Genshin, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, _, imageRepoMock, wikiApiMock) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync<CharacterListData>("CharList_TestData_1.json");
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

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

        var charDetail = new GenshinCharacterDetail()
        {
            List =
            [
                new()
                {
                    Base = new()
                    {
                        Id = 10000089,
                        Name = "Furina",
                        Level = 90,
                        Icon = "https://icon.png",
                        Image = "https://image.png",
                        Weapon = new()
                        {
                            Id = 11401,
                            Icon = "https://icon.png",
                            Name = "Favonius Sword",
                            Level = 90
                        }
                    },
                    Weapon = new() {
                        Id = 11401,
                        Icon = "https://icon.png",
                        Name = "Favonius Sword",
                        Level = 90,
                        TypeName = "Sword",
                        Type = 1,
                        MainProperty = new() { Base = "100", Final = "200" },
                        PromoteLevel = 6
                    },
                    Relics = [],
                    Constellations = [],
                    SelectedProperties = [],
                    BaseProperties = [],
                    ExtraProperties = [],
                    ElementProperties = [],
                    Skills = []
                }
            ],
            AvatarWiki = [],
            WeaponWiki = new()
            {
                { "11401", "https://wiki/Favonius_Sword" }
            }
        };

        characterApiMock.Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(charDetail));

        wikiApiMock.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>())).ReturnsAsync(Result<JsonNode>.Success(wikiResponse!));
        imageRepoMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        imageUpdaterMock.Setup(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<GenshinBasicCharacterData>>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        // Verify avatar and weapon images were updated
        var expectedImageCount = charList.List!.Count * 2; // Avatar + Weapon for each character
        var expectedAscendedCount = charList.List.DistinctBy(c => c.Weapon.Id).Count(c => c.Weapon.Level > 40);

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.Exactly(expectedImageCount));
        imageUpdaterMock
            .Verify(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()), Times.Exactly(expectedAscendedCount));
    }

    [Test]
    public async Task ExecuteAsync_WithAscendedWeapons_UpdatesWeaponImages()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, _, imageRepositoryMock, wikiApiMock) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        // Create a character with weapon level > 40
        var charList = new List<GenshinBasicCharacterData>
        {
            new()
            {
                Id = 10000001,
                Name = "TestChar",
                Icon = "http://icon",
                Weapon = new()
                {
                    Id = 11101,
                    Name = "TestWeapon",
                    Level = 50,
                    Icon = "http://icon"
                }
            }
        };

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        // Image does not exist
        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        // Setup Character Detail
        var charDetail = new GenshinCharacterDetail
        {
            AvatarWiki = [],
            List =
            [
                new()
                {
                    Base = new BaseCharacterDetail
                    {
                        Id = 10000001,
                        Name = "TestChar",
                        Icon = "http://icon",
                        Image = "http://image",
                        Weapon = new() { Id = 11101, Name = "TestWeapon", Icon = "http://icon" }
                    },
                    Weapon = new WeaponDetail
                    {
                        Id = 11101,
                        Name = "TestWeapon",
                        Level = 50,
                        PromoteLevel = 2,
                        Type = 1,
                        Icon = "http://icon",
                        TypeName = "Sword",
                        MainProperty = new StatProperty { Base = "100", Final = "200" }
                    },
                    Relics = [],
                    Constellations = [],
                    SelectedProperties = [],
                    BaseProperties = [],
                    ExtraProperties = [],
                    ElementProperties = [],
                    Skills = []
                }
            ],
            WeaponWiki = new Dictionary<string, string>
            {
                { "11101", "https://wiki/TestWeapon" }
            }
        };

        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(charDetail));

        // Setup Wiki Api
        var wikiJson = JsonNode.Parse("{\"data\": {\"page\": {\"modules\": [{\"components\": [{\"component_id\": \"gallery_character\", \"data\": \"{\\\"list\\\": [{\\\"img\\\": \\\"url1\\\"}, {\\\"img\\\": \\\"ascended_url\\\"}]}\"}]}]}}}");

        wikiApiMock
            .Setup(x => x.GetAsync(It.IsAny<WikiApiContext>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiJson!));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        imageUpdaterMock
            .Setup(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<GenshinBasicCharacterData>>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
        {
            LtUid = 1ul,
            LToken = "test"
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert
        // Verify UpdateMultiImageAsync was called for the weapon
        imageUpdaterMock.Verify(x => x.UpdateMultiImageAsync(
            It.Is<IMultiImageData>(d => d.Name.Equals("genshin_weapon_ascended_11101") && d.AdditionalUrls.Contains("ascended_url")),
            It.IsAny<IMultiImageProcessor>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, userRepositoryMock, _, _, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
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

        // Force early exit after UpdateGameUid by making char API fail
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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
        var (service, characterApiMock, _, gameRoleApiMock, _, userRepositoryMock, _, _, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
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

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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
        var (service, characterApiMock, _, gameRoleApiMock, _, userRepositoryMock, _, _, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        // Case: user not found
        userRepositoryMock
            .Setup(x => x.GetUserAsync(1ul))
            .ReturnsAsync((UserModel?)null);

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = new GenshinCharListApplicationContext(1, ("server", Server.Asia.ToString()))
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
    [TestCase("CharList_TestData_1.json")]
    [TestCase("CharList_TestData_2.json")]
    [TestCase("CharList_TestData_3.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _) = SetupIntegrationTest();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync<CharacterListData>(testDataFile);
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

        var context = new GenshinCharListApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(), ("server", Server.Asia.ToString()))
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
            $"CharListIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

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

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var service = SetupRealApiIntegrationTest();

        var context = new GenshinCharListApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(), ("server", Server.Asia.ToString()))
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
            var outputImagePath = Path.Combine(outputDirectory, "CharListRealApi.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        GenshinCharListApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<IEnumerable<GenshinBasicCharacterData>>> CardServiceMock,
        Mock<IUserRepository> UserRepositoryMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock
        ) SetupMocks()
    {
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<IEnumerable<GenshinBasicCharacterData>>>();
        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail,
            GenshinCharacterApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var loggerMock = new Mock<ILogger<GenshinCharListApplicationService>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();

        // Default setup for CharacterDetail to avoid null reference in ExecuteAsync
        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError, "Default Mock Behavior"));

        var service = new GenshinCharListApplicationService(
            imageUpdaterMock.Object,
            cardServiceMock.Object,
            characterApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            characterCacheMock.Object,
            imageRepositoryMock.Object,
            wikiApiMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, userRepositoryMock,
            characterCacheMock, imageRepositoryMock, wikiApiMock);
    }

    private static (
        GenshinCharListApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IUserRepository> UserRepositoryMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new GenshinCharListCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharListCardService>>());

        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData,
            GenshinCharacterDetail, GenshinCharacterApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinCharListApplicationService>>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var characterCacheMock = new Mock<ICharacterCacheService>();

        var imageRepository = MongoTestHelper.Instance.ImageRepository;
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();

        var service = new GenshinCharListApplicationService(
            imageUpdaterService,
            cardService,
            characterApiMock.Object,
            gameRoleApiMock.Object,
            userRepositoryMock.Object,
            characterCacheMock.Object,
            imageRepository,
            wikiApiMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, userRepositoryMock, characterCacheMock, wikiApiMock);
    }

    private static GenshinCharListApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new GenshinCharListCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharListCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Simple in-memory cache service for tests
        var cacheServiceMock = new Mock<ICacheService>();
        // Return null for all cache gets (no caching behavior in test)
        cacheServiceMock
            .Setup(x => x.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<GenshinBasicCharacterData>?)null);

        // Real character API service
        var characterApiService = new GenshinCharacterApiService(
            cacheServiceMock.Object,
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinCharacterApiService>>());

        // Real game role API service
        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        // Real image updater service
        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var userRepositoryMock = new Mock<IUserRepository>();
        var characterCacheMock = new Mock<ICharacterCacheService>();

        var imageRepositoryMock = new Mock<IImageRepository>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();

        var service = new GenshinCharListApplicationService(
            imageUpdaterService,
            cardService,
            characterApiService,
            gameRoleApiService,
            userRepositoryMock.Object,
            characterCacheMock.Object,
            imageRepositoryMock.Object,
            wikiApiMock.Object,
            Mock.Of<ILogger<GenshinCharListApplicationService>>());

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

    private static async Task<T> LoadTestDataAsync<T>(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<T>(json);
        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
