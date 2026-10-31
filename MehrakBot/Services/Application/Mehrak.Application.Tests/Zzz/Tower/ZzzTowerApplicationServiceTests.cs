using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Application.Zzz.Tower;
using Mehrak.Domain.Card;
using Mehrak.Domain.Character;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Abstractions;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Zzz.Tower;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzTowerApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

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
        var (service, _, _, _, _, gameRoleApiMock, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
        }
    }

    [TestCase(StatusCode.ExternalServerError)]
    [TestCase(StatusCode.Timeout)]
    [TestCase(StatusCode.Cancelled)]
    public async Task ExecuteAsync_TowerApiError_ReturnsApiError(StatusCode status)
    {
        // Arrange
        var (service, towerApiMock, _, _, _, gameRoleApiMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Failure(status, "unsafe upstream text"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(status == StatusCode.ExternalServerError ? CommandFailureReason.ApiError : CommandFailureReason.Timeout));
            Assert.That(result.Error, Is.EqualTo(status == StatusCode.ExternalServerError ? new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.SimulatedBattleTrial) : new CommandError(CommandErrorCode.Timeout, CommandErrorTarget.None)));
            Assert.That(result.Error!.Message, Does.Not.Contain("unsafe upstream text"));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoData_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, towerApiMock, _, _, _, gameRoleApiMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(GetEmptyData()));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            AssertEmptyOutcome(result, OperationOutcome.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_CharacterApiError_ReturnsApiError()
    {
        // Arrange
        var (service, towerApiMock, characterApiMock, _, _, gameRoleApiMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(await LoadTestDataAsync("Tower_TestData_1.json")));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.CharacterList)));
        }
    }

    [Test]
    public async Task ExecuteAsync_AttachmentExists_ReturnsCachedAttachment()
    {
        // Arrange
        var (service, towerApiMock, characterApiMock, imageUpdaterMock, cardServiceMock, gameRoleApiMock,
            attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(await LoadTestDataAsync("Tower_TestData_1.json")));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(GetOutcome(result).Image, Is.Not.Null);
        }

        cardServiceMock.Verify(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzTowerData>>()), Times.Never);
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()), Times.Never);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, towerApiMock, characterApiMock, imageUpdaterMock, _, gameRoleApiMock, attachmentStorageMock, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(await LoadTestDataAsync("Tower_TestData_1.json")));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image)));
        }
    }

    [TestCase(Server.Asia)]
    [TestCase(Server.Europe)]
    [TestCase(Server.America)]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(Server server)
    {
        // Arrange
        var (service, towerApiMock, characterApiMock, imageUpdaterMock, cardServiceMock, gameRoleApiMock,
            attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(await LoadTestDataAsync("Tower_TestData_1.json")));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzTowerData>>()))
            .ReturnsAsync(new MemoryStream());

        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("server", server.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(GetOutcome(result).Image, Is.Not.Null);
            Assert.That(GetOutcome(result).Mode, Is.EqualTo(EndgameMode.SimulatedBattleTrial));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(GetOutcome(result).CycleStart, Is.Null);
        Assert.That(GetOutcome(result).CycleEnd, Is.Null);

        // A cache hit must return the exact same business facts and actual storage key.
        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var imageCalls = imageUpdaterMock.Invocations.Count;
        var cached = await service.ExecuteAsync(context);
        Assert.That(imageUpdaterMock.Invocations.Count, Is.EqualTo(imageCalls));
        Assert.That(cached.IsSuccess, Is.True);
        Assert.That(cached.Outcome, Is.EqualTo(result.Outcome));
        Assert.That(GetOutcome(cached).Image!.SourceType, Is.EqualTo(AttachmentSourceType.AttachmentStorage));
        Assert.That(GetOutcome(cached).Image!.StorageFileName, Does.Match("^[a-f0-9]{64}\\.jpg$"));
        var finalKey = GetOutcome(result).Image!.StorageFileName;
        attachmentStorageMock.Verify(x => x.StoreAsync(finalKey, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(Server.Asia)]
    public async Task ExecuteAsync_AttachmentStorageFailure_ReturnsSafeCodedError(Server server)
    {
        // Arrange
        var (service, towerApiMock, characterApiMock, imageUpdaterMock, cardServiceMock, gameRoleApiMock,
            attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(await LoadTestDataAsync("Tower_TestData_1.json")));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzTowerData>>()))
            .ReturnsAsync(new MemoryStream());

        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("server", server.ToString()));

        // Act
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await service.ExecuteAsync(context);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.AttachmentStoreFailed, CommandErrorTarget.Attachment)));
            Assert.That(result.Outcome, Is.Null);
        }
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ChangedCharacterMap_DoesNotReuseTheOldCardName()
    {
        var (service, towerApiMock, characterApiMock, imageUpdaterMock, cardServiceMock, gameRoleApiMock,
            attachmentStorageMock, _) = SetupMocks();
        var profile = CreateTestProfile();
        var existsNames = new List<string>();
        var characterCall = 0;

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));
        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Success(await LoadTestDataAsync("Tower_TestData_1.json")));
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(() =>
            {
                characterCall++;
                var characters = CreateBasicCharacterList(characterCall == 1 ? 1 : 2);
                return Result<IEnumerable<ZzzBasicAvatarData>>.Success(characters);
            });
        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string name, CancellationToken _) => existsNames.Add(name))
            .ReturnsAsync(false);
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzTowerData>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Stream>(new MemoryStream()));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        await service.ExecuteAsync(context);
        await service.ExecuteAsync(context);

        Assert.That(existsNames, Has.Count.EqualTo(2));
        Assert.That(existsNames[0], Is.Not.EqualTo(existsNames[1]));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, towerApiMock, _, _, _, gameRoleApiMock, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        towerApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
            Assert.That(stored.Region, Is.EqualTo(Server.Asia.ToString()));
            Assert.That(stored.Game, Is.EqualTo(Game.ZenlessZoneZero));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, towerApiMock, _, _, _, gameRoleApiMock, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var seededProfile = SeedUserProfile(userContext, 1ul, 1, 1ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = seededProfile.Id,
            Game = Game.ZenlessZoneZero,
            Region = Server.Asia.ToString(),
            GameUid = profile.GameUid
        });
        await userContext.SaveChangesAsync();

        towerApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
        Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, towerApiMock, _, _, _, gameRoleApiMock, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        towerApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        SeedUserProfile(userContext, 1ul, 2, 99999ul);

        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    #endregion

    #region Helpers

    private (
        ZzzTowerApplicationService Service,
        Mock<IApiService<ZzzTowerData, BaseHoYoApiContext>> TowerApiMock,
        Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>> CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<ZzzTowerData>> CardServiceMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<ZzzTowerData>>();
        var towerApiMock = new Mock<IApiService<ZzzTowerData, BaseHoYoApiContext>>();
        var characterApiMock = new Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<ZzzTowerApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new ZzzTowerApplicationService(
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            cardServiceMock.Object,
            towerApiMock.Object,
            characterApiMock.Object,
            imageUpdaterMock.Object,
            loggerMock.Object);

        return (service, towerApiMock, characterApiMock, imageUpdaterMock, cardServiceMock, gameRoleApiMock,
            attachmentStorageMock, userContext);
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
            GameUid = "1000000000",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static List<ZzzBasicAvatarData> CreateBasicCharacterList(int firstRank = 1)
    {
        return
        [
            new ZzzBasicAvatarData
            {
                Id = 1011,
                Name = "Anby",
                FullName = "Anby Demara",
                CampName = "Unknown",
                Rarity = "A",
                GroupIconPath = "icon",
                HollowIconPath = "hollow",
                RoleSquareUrl = "url",
                AwakenState = "0",
                Level = 60,
                Rank = firstRank
            },
            new ZzzBasicAvatarData
            {
                Id = 1091,
                Name = "Miyabi",
                FullName = "Hoshimi Miyabi",
                CampName = "Section 6",
                Rarity = "S",
                GroupIconPath = "icon",
                HollowIconPath = "hollow",
                RoleSquareUrl = "url",
                AwakenState = "0",
                Level = 60,
                Rank = 6
            }
        ];
    }

    private static async Task<ZzzTowerData> LoadTestDataAsync(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<ZzzTowerData>(json);

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

    private static ZzzTowerData GetEmptyData()
    {
        return new() { DisplayAvatarRankList = [], LayerInfo = new() { MedalIcon = string.Empty }, MvpInfo = new() };
    }

    #endregion
    private static EndgameOutcome GetOutcome(CommandResult result)
    {
        Assert.That(result.Outcome, Is.TypeOf<EndgameOutcome>());
        var outcome = (EndgameOutcome)result.Outcome!;
        Assert.That(outcome.Game, Is.EqualTo(Game.ZenlessZoneZero));
        Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.SimulatedBattleTrial));
        Assert.That(outcome.GameUid, Is.Not.Empty);
        Assert.That(result.ToProto().ToDomain().Outcome, Is.EqualTo(outcome));
        Assert.That(outcome.CycleStart, Is.Null);
        Assert.That(outcome.CycleEnd, Is.Null); if (outcome.State == OperationOutcome.Complete)
        {
            Assert.That(outcome.Image, Is.Not.Null);
            Assert.That(outcome.Image!.SourceType, Is.EqualTo(AttachmentSourceType.AttachmentStorage));
            Assert.That(outcome.Image.StorageFileName, Does.Match("^[a-f0-9]{64}\\.jpg$"));
        }
        Assert.That(outcome.Floor, Is.Null);

        return outcome;
    }

    private static void AssertEmptyOutcome(CommandResult result, OperationOutcome state)
    {
        var outcome = GetOutcome(result);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.State, Is.EqualTo(state));
            Assert.That(outcome.GameUid, Is.EqualTo(CreateTestProfile().GameUid));
            Assert.That(outcome.Image, Is.Null);
            Assert.That(outcome.CycleStart, Is.Null);
            Assert.That(outcome.CycleEnd, Is.Null);
        }
    }
}
