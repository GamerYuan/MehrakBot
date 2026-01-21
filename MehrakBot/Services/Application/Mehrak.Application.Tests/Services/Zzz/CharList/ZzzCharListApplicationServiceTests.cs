using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Zzz.CharList;
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

namespace Mehrak.Application.Tests.Services.Zzz.CharList;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzCharListApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;

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
        var (service, _, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();
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
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

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
    public async Task ExecuteAsync_UpdatesCharacterCache_WhenCharacterListFetched()
    {
        // Arrange
        var (service, characterApiMock, buddyApiMock, characterCacheMock, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = CreateBasicCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(charList));

        // Fail buddy api to stop execution early
        buddyApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        characterCacheMock.Verify(x => x.UpsertCharacters(
                Game.ZenlessZoneZero,
                It.Is<IEnumerable<string>>(names => names.Contains("Jane"))),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_BuddyListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, buddyApiMock, _, _, _, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        buddyApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBuddyData>>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Bangboo List"));
        }
    }

    [Test]
    public async Task ExecuteAsync_AttachmentExists_ReturnsCachedAttachment()
    {
        // Arrange
        var (service, characterApiMock, buddyApiMock, _, _, _, attachmentStorageMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        buddyApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBuddyData>>.Success(CreateBuddyList()));

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

        // Should NOT generate card
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsBotError()
    {
        // Arrange
        var (service, characterApiMock, buddyApiMock, _, imageUpdaterMock, _, attachmentStorageMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        buddyApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBuddyData>>.Success(CreateBuddyList()));

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false); // Fail image update

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
            Assert.That(result.ErrorMessage, Does.Contain("An error occurred while retrieving images"));
        }
    }

    [Test]
    public async Task ExecuteAsync_Success_GeneratesCardAndStoresResult()
    {
        // Arrange
        var (service, characterApiMock, buddyApiMock, _, imageUpdaterMock, cardServiceMock, attachmentStorageMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Success(CreateBasicCharacterList()));

        buddyApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBuddyData>>.Success(CreateBuddyList()));

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>>()))
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
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, _, _, _, _, gameRoleApiMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        // Force early exit after UpdateGameUid using char API fail
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<ZzzBasicAvatarData>>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
            Assert.That(stored.Region, Is.EqualTo(Server.Asia.ToString()));
            Assert.That(stored.Game, Is.EqualTo(Game.ZenlessZoneZero));
        }
    }

    #endregion

    #region Helpers

    private (
        ZzzCharListApplicationService Service,
        Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>> CharacterApiMock,
        Mock<IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext>> BuddyApiMock,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>> CardServiceMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var characterApiMock = new Mock<ICharacterApiService<ZzzBasicAvatarData, ZzzFullAvatarData, CharacterApiContext>>();
        var buddyApiMock = new Mock<IApiService<IEnumerable<ZzzBuddyData>, BaseHoYoApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<ZzzCharListApplicationService>>();

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new ZzzCharListApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            characterApiMock.Object,
            buddyApiMock.Object,
            characterCacheMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, buddyApiMock, characterCacheMock, imageUpdaterMock, cardServiceMock,
            attachmentStorageMock, gameRoleApiMock, userContext);
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
            }
        ];
    }

    private static List<ZzzBuddyData> CreateBuddyList()
    {
        return
        [
            new ZzzBuddyData
            {
                Id = 1001,
                Name = "Butler",
                Rarity = "S",
                Star = 1,
                Level = 60,
                BangbooSquareUrl = "url"
            }
        ];
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
