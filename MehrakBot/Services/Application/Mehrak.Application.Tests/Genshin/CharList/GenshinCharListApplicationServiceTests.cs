#region

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mehrak.Application.Genshin.CharList;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Abstractions;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Wiki;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Genshin.CharList;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinCharListApplicationServiceTests
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
        var (service, _, _, gameRoleApiMock, _, _, _, _, _, _) = SetupMocks();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

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
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character List"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, _, _, imageRepoMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync<CharacterListData>("CharList_TestData_1.json");
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

        imageRepoMock.Setup(x => x.FileExistsAsync(It.IsAny<string>())).ReturnsAsync((string val, CancellationToken _) => val.Contains("weapon"));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

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
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, characterCacheMock, imageRepoMock, _, attachmentStorageMock, _) = SetupMocks();

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

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

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
        characterCacheMock.Verify(x => x.UpsertCharacters(Game.Genshin, It.IsAny<IEnumerable<CharacterUpsertEntry>>()), Times.Once);
        foreach (var weaponName in charList.List!.Where(x => x.Weapon.Level > 40)
                     .Select(x => x.Weapon.ToAscendedImageName()).Distinct())
            imageRepoMock.Verify(x => x.FileExistsAsync(weaponName, It.IsAny<CancellationToken>()), Times.Once);
        characterApiMock.Verify(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, imageRepoMock, wikiApiMock, _, _) = SetupMocks();

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

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var expectedImageCount = charList.List!.DistinctBy(x => x.ToImageName()).Count()
            + charList.List.DistinctBy(x => x.Weapon.ToBaseImageName()).Count();
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
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, imageRepositoryMock,
            wikiApiMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

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

        imageRepositoryMock
            .Setup(x => x.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

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

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        imageUpdaterMock.Verify(x => x.UpdateMultiImageAsync(
            It.Is<IMultiImageData>(d => d.Name.Equals("genshin/weapon_ascended_11101.png") && d.AdditionalUrls.Contains("ascended_url")),
            It.IsAny<IMultiImageProcessor>()), Times.Once);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_Level40SharedWeapon_UsesPerCharacterAscensionAndSkipsExistingArtwork(bool assetExists)
    {
        var (service, characterApi, updater, profileApi, cardService, _, repository, wiki, _, _) = SetupMocks();
        var characters = Enumerable.Range(1, 2).Select(id => new GenshinBasicCharacterData
        {
            Id = id, Name = $"Character{id}", Icon = "https://example.com/avatar.png",
            Weapon = new Weapon { Id = 11101, Name = "Weapon", Icon = "https://example.com/weapon.png", Level = 40 }
        }).ToList();
        profileApi.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        characterApi.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(characters));
        var detail = new GenshinCharacterDetail
        {
            List = characters.Select((character, index) => new GenshinCharacterInformation
            {
                Base = new BaseCharacterDetail
                {
                    Id = character.Id!.Value, Name = character.Name, Icon = character.Icon,
                    Image = character.Icon, Weapon = character.Weapon
                },
                Weapon = new WeaponDetail
                {
                    Id = 11101, Name = "Weapon", Icon = "https://example.com/weapon.png",
                    Level = 40, PromoteLevel = index == 0 ? 2 : 1, TypeName = "Sword",
                    MainProperty = new StatProperty { Base = "100", Final = "100" }
                },
                Relics = [], Constellations = [], SelectedProperties = [], BaseProperties = [],
                ExtraProperties = [], ElementProperties = [], Skills = []
            }).ToList(),
            AvatarWiki = [], WeaponWiki = new() { ["11101"] = "https://example.com/wiki/11101" }
        };
        characterApi.Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Success(detail));
        repository.Setup(x => x.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(assetExists);
        updater.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        updater.Setup(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var gallery = JsonSerializer.Serialize(new { list = new[] { new { img = "base" }, new { img = "ascended" } } });
        var wikiData = JsonSerializer.SerializeToNode(new
        {
            data = new { page = new { modules = new[] { new { components = new[] { new { component_id = "gallery_character", data = gallery } } } } } }
        });
        wiki.Setup(x => x.GetAsync(It.IsAny<WikiApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JsonNode>.Success(wikiData!));
        cardService.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<GenshinBasicCharacterData>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream());

        var result = await service.ExecuteAsync(CreateContext(1, 1, "test", ("server", Server.Asia.ToString())));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(characters[0].Weapon.Ascended, Is.True);
        Assert.That(characters[1].Weapon.Ascended, Is.False);
        characterApi.Verify(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.FileExistsAsync("genshin/weapon_ascended_11101.png", It.IsAny<CancellationToken>()), Times.Once);
        wiki.Verify(x => x.GetAsync(It.IsAny<WikiApiContext>(), It.IsAny<CancellationToken>()), assetExists ? Times.Never() : Times.Once());
        updater.Verify(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>(), It.IsAny<CancellationToken>()),
            assetExists ? Times.Never() : Times.Once());
        updater.Verify(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

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
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _, _, userContext) = SetupMocks();

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

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
            Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        SeedUserProfile(userContext, 1ul, 2, 99999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_ProfileApiTimeout_ReturnsTimeoutError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _, _, _, _, _, _) = SetupMocks();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Timeout, "timed out"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.Timeout));
        }
    }

    [Test]
    public async Task ExecuteAsync_AlwaysFetchesProfileAndUsesCachedUid_WhenGameUidCached()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, imageRepositoryMock,
            _, attachmentStorageMock, userContext) = SetupMocks();

        const string cachedUid = "777777777";
        const string apiUid = "800000000";

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(new GameProfileDto
            {
                GameUid = apiUid,
                Nickname = "TestPlayer",
                Level = 60
            }));

        var seededProfile = SeedUserProfile(userContext, 1ul, 1, 1ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = seededProfile.Id,
            Game = Game.Genshin,
            Region = Server.Asia.ToString(),
            GameUid = cachedUid
        });
        await userContext.SaveChangesAsync();

        var charList = await LoadTestDataAsync<CharacterListData>("CharList_TestData_1.json");
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

        imageRepositoryMock.Setup(x => x.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>())).ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<GenshinBasicCharacterData>>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            // Intended behavior: the GameRole profile API is always called, even when the GameUid is cached.
            gameRoleApiMock.Verify(x => x.GetAsync(It.IsAny<GameRoleApiContext>()), Times.Once);
            // The cached GameUid is used to start the primary call in parallel with the profile fetch.
            characterApiMock.Verify(x => x.GetAllCharactersAsync(
                It.Is<GenshinCharacterApiContext>(c => c.GameUid == cachedUid)), Times.Once);
            // GameUid is not re-saved when it is already cached.
            Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var (service, characterApiMock, _, gameRoleApiMock, _, _, attachmentStorageMock, storedAttachments, _) = SetupIntegrationTest();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync<CharacterListData>(testDataFile);
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList.List!));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        }
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));

        var attachment = result.Data.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(!string.IsNullOrWhiteSpace(attachment!.FileName));

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment.FileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        if (storedAttachments.TryGetValue(attachment.FileName, out var stored))
        {
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(
                outputDirectory,
                $"CharListIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

            stored.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await stored.CopyToAsync(fileStream);
        }
    }

    [Test]
    [NonParallelizable]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        // Cold benchmark mode: faithful empty-dynamic-S3 measurement selected by
        // MEHRAK_CHARLIST_BENCHMARK=1. Only the parent harness runs this path with
        // existing credentials from appsettings.test.json. Ordinary
        // runs keep the historical file-based behavior below unchanged.
        if (CharListBenchmarkSupport.IsBenchmarkMode())
        {
            await RunColdBenchmarkAsync();
            return;
        }

        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        }

        var (service, storedAttachments, _) = SetupRealApiIntegrationTest();

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), testLtUid, testLToken!,
            ("server", Server.Asia.ToString()));

        var result = await service.ExecuteAsync(context);

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
            var outputImagePath = Path.Combine(outputDirectory, "CharListRealApi.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Cold Benchmark (MEHRAK_CHARLIST_BENCHMARK=1)

    /// <summary>
    /// Faithful cold benchmark for the charlist flow. Only the parent harness runs
    /// this with existing credentials from appsettings.test.json.
    /// Measured flow uses real GameRole/character/wiki APIs, real downloads, real
    /// weapon processing through loopback gRPC, and real local-S3 storage starting
    /// from empty dynamic assets. Character autocomplete upsert stays mocked and is
    /// excluded by design. All assertions and output are secret-safe: no game UID,
    /// nickname, token, URL, character payload, or image bytes are ever printed.
    /// No private JPGs are written to disk in benchmark mode.
    /// </summary>
    private async Task RunColdBenchmarkAsync()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json").Build();
        using var configLifetime = config as IDisposable;
        var credentials = config.GetRequiredSection("Credentials");
        var lToken = credentials["LToken"];
        Assert.That(ulong.TryParse(credentials["LtUid"], out var ltUid) && ltUid > 0, Is.True,
            "Benchmark credentials are missing from appsettings.test.json.");
        Assert.That(string.IsNullOrEmpty(lToken), Is.False,
            "Benchmark credentials are missing from appsettings.test.json.");

        // Must match the production RegionUtility mapping for Server.Asia.
        const string benchmarkRegion = "os_asia";
        const Server benchmarkServer = Server.Asia;

        // Setup is excluded from measurement: fresh existence cache, one shared
        // real repository, cold dynamic S3 with only static element icons.
        using var existsCache = new MemoryCache(new MemoryCacheOptions());
        var imageRepository = S3TestHelper.Instance.CreateImageRepository(existsCache);
        var attachmentStorage = S3TestHelper.Instance.CreateAttachmentStorage();

        Task<List<string>> ListKeysAsync(string prefix) => CharListBenchmarkSupport.ListKeysAsync(
            S3TestHelper.Instance.S3Client, S3TestHelper.Instance.BucketName, prefix);

        var initialAvatars = await ListKeysAsync("genshin/avatar_");
        var initialBases = await ListKeysAsync("genshin/weapon_base_");
        var initialAscended = await ListKeysAsync("genshin/weapon_ascended_");
        var initialDynamicCount = initialAvatars.Count + initialBases.Count + initialAscended.Count;
        if (initialDynamicCount != 0)
        {
            FailBenchmark("Benchmark cold state is not empty.");
            return;
        }
        var elementIcons = await ListKeysAsync("genshin/element_");
        Assert.That(elementIcons.Count, Is.EqualTo(CharListBenchmarkSupport.StaticElements.Length),
            "Benchmark static element icons are incomplete.");

        using var httpClientFactory = new BenchmarkHttpClientFactory();
        // Fresh per-run API caches: empty at start, shared across services like production.
        var apiCache = new InMemoryBenchmarkCacheService();
        var gameRoleApi = new GameRoleApiService(
            httpClientFactory, apiCache, NullLogger<GameRoleApiService>.Instance);
        var characterApi = new GenshinCharacterApiService(
            apiCache, httpClientFactory, NullLogger<GenshinCharacterApiService>.Instance);
        var wikiApi = new WikiApiService(
            httpClientFactory, NullLogger<WikiApiService>.Instance);
        var imageUpdater = new ImageUpdaterService(
            imageRepository, httpClientFactory, NullLogger<ImageUpdaterService>.Instance);

        // Real weapon processing: production processor through loopback HTTP/2 gRPC.
        await using var weaponHost = new WeaponGrpcBenchmarkHost();
        await weaponHost.StartAsync();
        var weaponProcessor = new WeaponImageProcessorGrpcClient(
            new Mehrak.Domain.Protobuf.ImageProcessorService.ImageProcessorServiceClient(weaponHost.Channel),
            NullLogger<WeaponImageProcessorGrpcClient>.Instance);

        var metrics = new CharListBenchmarkMetrics();
        var cardService = new GenshinCharListCardService(
            imageRepository,
            NullLogger<GenshinCharListCardService>.Instance,
            metrics);
        await cardService.InitializeAsync();

        var characterCacheMock = new Mock<ICharacterCacheService>();

        // Fresh SQLite database per run: no cached game UID, like a first-ever request.
        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinCharListApplicationService(
            imageUpdater,
            cardService,
            characterApi,
            gameRoleApi,
            userContext,
            characterCacheMock.Object,
            imageRepository,
            wikiApi,
            attachmentStorage,
            weaponProcessor,
            NullLogger<GenshinCharListApplicationService>.Instance);

        var context = CreateContext(
            S3TestHelper.Instance.GetUniqueUserId(), ltUid, lToken!, ("server", benchmarkServer.ToString()));

        var stopwatch = Stopwatch.StartNew();
        CommandResult result;
        try
        {
            result = await service.ExecuteAsync(context);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            FailBenchmark("Benchmark run raised an unclassified error.");
            return;
        }
        stopwatch.Stop();

        if (!result.IsSuccess)
        {
            FailBenchmark("Benchmark run did not succeed.");
            return;
        }

        var attachment = result.Data?.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null, "Benchmark run produced no attachment.");
        Assert.That(attachmentStorage.IsValidStorageFileName(attachment!.FileName), Is.True,
            "Benchmark attachment name is invalid.");

        // Real persistence check, outside the measured interval.
        var download = await attachmentStorage.DownloadAsync(attachment.FileName);
        Assert.That(download, Is.Not.Null, "Benchmark attachment was not persisted.");
        await using var downloadedContent = download!.Content;
        var outputBytes = downloadedContent.Length;
        Assert.That(outputBytes, Is.GreaterThan(0), "Benchmark attachment is empty.");

        // Roster reload is served from this run's own API caches (warm by construction).
        var profileResult = await gameRoleApi.GetAsync(
            new GameRoleApiContext(context.UserId, ltUid, lToken!, Game.Genshin, benchmarkRegion));
        Assert.That(profileResult.IsSuccess, Is.True, "Benchmark verification could not reload the game profile.");
        var verifiedGameUid = profileResult.Data?.GameUid;
        Assert.That(string.IsNullOrEmpty(verifiedGameUid), Is.False, "Benchmark verification found no game profile.");
        var listResult = await characterApi.GetAllCharactersAsync(
            new GenshinCharacterApiContext(context.UserId, ltUid, lToken!, verifiedGameUid, benchmarkRegion));
        Assert.That(listResult.IsSuccess, Is.True, "Benchmark verification could not reload the character roster.");
        var roster = listResult.Data?.ToList() ?? [];
        Assert.That(roster.Count, Is.GreaterThan(0), "Benchmark roster is empty.");

        var uniqueWeaponIds = roster
            .Where(x => x.Weapon.Id.HasValue)
            .Select(x => x.Weapon.Id!.Value)
            .Distinct()
            .ToList();
        var level40Count = roster.Count(x => x.Weapon.Level == 40);
        var expectedAscendedIds = roster
            .Where(x => (x.Weapon.Level ?? 0) > 40 && x.Weapon.Id.HasValue)
            .Select(x => x.Weapon.Id!.Value)
            .Distinct()
            .ToList();
        var fingerprint = CharListBenchmarkSupport.ComputeRosterFingerprint(roster.Select(x => new CharListRosterEntry(
            x.Id ?? 0, x.Level ?? 0, x.Rarity ?? 0, x.Element ?? "unknown",
            x.Weapon.Id ?? 0, x.Weapon.Level ?? 0, x.Weapon.Rarity ?? 0,
            x.ActivedConstellationNum ?? 0, x.Weapon.AffixLevel ?? 0)));

        var storedAvatars = await ListKeysAsync("genshin/avatar_");
        var storedBases = await ListKeysAsync("genshin/weapon_base_");
        var storedAscended = await ListKeysAsync("genshin/weapon_ascended_");
        var storedAscendedIds = storedAscended
            .Select(ParseWeaponAscendedAssetId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
        var missingAscended = expectedAscendedIds.Count(x => !storedAscendedIds.Contains(x));

        Assert.That(storedAvatars.Count, Is.EqualTo(roster.Count), "Benchmark avatar asset count mismatch.");
        Assert.That(storedBases.Count, Is.EqualTo(uniqueWeaponIds.Count), "Benchmark weapon asset count mismatch.");
        Assert.That(metrics.LastCardDurationMs, Is.GreaterThanOrEqualTo(0), "Benchmark card timer did not record.");
        if (expectedAscendedIds.Count > 0)
            Assert.That(storedAscended.Count, Is.GreaterThan(0), "Benchmark stored no ascended weapon assets.");

        var line = CharListBenchmarkSupport.BuildBenchmarkLine(
            "ok",
            stopwatch.Elapsed.TotalMilliseconds,
            metrics.LastCardDurationMs,
            roster.Count,
            uniqueWeaponIds.Count,
            level40Count,
            fingerprint,
            initialDynamicCount,
            outputBytes,
            storedAvatars.Count,
            storedBases.Count,
            storedAscended.Count,
            expectedAscendedIds.Count,
            missingAscended);
        Assert.That(line.Any(x => x is '\r' or '\n'), Is.False, "Benchmark line must be a single line.");
        TestContext.Progress.WriteLine($"{CharListBenchmarkSupport.BenchmarkLinePrefix} {line}");
    }

    private static void FailBenchmark(string message)
    {
        TestContext.Progress.WriteLine(
            $"{CharListBenchmarkSupport.BenchmarkLinePrefix} {CharListBenchmarkSupport.BuildBenchmarkErrorLine()}");
        Assert.Fail(message);
    }

    private static int? ParseWeaponAscendedAssetId(string key)
    {
        var file = key.Split('/')[^1];
        var idPart = file.Replace("weapon_ascended_", string.Empty).Replace(".png", string.Empty);
        return int.TryParse(idPart, out var id) ? id : null;
    }

    #endregion

    #region Helper Methods

    private (
        GenshinCharListApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<IEnumerable<GenshinBasicCharacterData>>> CardServiceMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IImageRepository> ImageRepositoryMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<IEnumerable<GenshinBasicCharacterData>>>();
        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail,
            GenshinCharacterApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var loggerMock = new Mock<ILogger<GenshinCharListApplicationService>>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        characterApiMock
            .Setup(x => x.GetCharacterDetailAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<GenshinCharacterDetail>.Failure(StatusCode.ExternalServerError, "Default Mock Behavior"));

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinCharListApplicationService(
            imageUpdaterMock.Object,
            cardServiceMock.Object,
            characterApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            characterCacheMock.Object,
            imageRepositoryMock.Object,
            wikiApiMock.Object,
            attachmentStorageMock.Object,
            Mock.Of<IMultiImageProcessor>(),
            loggerMock.Object);

        return (Service: service, CharacterApiMock: characterApiMock, ImageUpdaterMock: imageUpdaterMock,
            GameRoleApiMock: gameRoleApiMock, CardServiceMock: cardServiceMock,
            CharacterCacheMock: characterCacheMock, ImageRepositoryMock: imageRepositoryMock,
            WikiApiMock: wikiApiMock, AttachmentStorageMock: attachmentStorageMock, UserContext: userContext);
    }

    private (
        GenshinCharListApplicationService Service,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IApiService<JsonNode, WikiApiContext>> WikiApiMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var cardService = new GenshinCharListCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharListCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var characterApiMock = new Mock<ICharacterApiService<GenshinBasicCharacterData,
            GenshinCharacterDetail, GenshinCharacterApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinCharListApplicationService>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var imageRepository = S3TestHelper.Instance.ImageRepository;
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
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

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinCharListApplicationService(
            imageUpdaterService,
            cardService,
            characterApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            characterCacheMock.Object,
            imageRepository,
            wikiApiMock.Object,
            attachmentStorageMock.Object,
            Mock.Of<IMultiImageProcessor>(),
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, characterCacheMock, wikiApiMock, attachmentStorageMock, storedAttachments, userContext);
    }

    private (GenshinCharListApplicationService Service, Dictionary<string, MemoryStream> StoredAttachments, UserDbContext UserContext) SetupRealApiIntegrationTest()
    {
        var cardService = new GenshinCharListCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharListCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.GetAsync<IEnumerable<GenshinBasicCharacterData>>(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<GenshinBasicCharacterData>?)null);

        var characterApiService = new GenshinCharacterApiService(
            cacheServiceMock.Object,
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinCharacterApiService>>());

        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ICacheService>(),
            Mock.Of<ILogger<GameRoleApiService>>());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();
        var imageRepositoryMock = new Mock<IImageRepository>();
        var wikiApiMock = new Mock<IApiService<JsonNode, WikiApiContext>>();
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

        var service = new GenshinCharListApplicationService(
            imageUpdaterService,
            cardService,
            characterApiService,
            gameRoleApiService,
            userContext,
            characterCacheMock.Object,
            imageRepositoryMock.Object,
            wikiApiMock.Object,
            attachmentStorageMock.Object,
            Mock.Of<IMultiImageProcessor>(),
            Mock.Of<ILogger<GenshinCharListApplicationService>>());

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

    private static async Task<T> LoadTestDataAsync<T>(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<T>(json);
        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
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
