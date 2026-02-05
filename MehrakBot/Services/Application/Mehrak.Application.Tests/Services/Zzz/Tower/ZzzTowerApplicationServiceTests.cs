using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Zzz.Tower;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Services.Zzz.Tower;

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

    [Test]
    public async Task ExecuteAsync_TowerApiError_ReturnsApiError()
    {
        // Arrange
        var (service, towerApiMock, _, _, _, gameRoleApiMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        towerApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Simulated Battle Trial"));
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
            .ReturnsAsync(Result<ZzzTowerData>.Success(null!));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
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
            Assert.That(result.ErrorMessage, Does.Contain("Character List"));
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
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
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
            Assert.That(result.ErrorMessage, Does.Contain("image").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_StoreAttachmentFails_ReturnsBotError()
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
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
        }
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
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

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>()
                    .Any(x => x.Content.Contains("Simulated Battle Trial Summary")),
                Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
                AwakenState = "0",
                Level = 60,
                Rank = 1
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
                AwakenState = "0",
                Level = 50,
                Rank = 2
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

    #endregion
}
