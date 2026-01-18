#region

using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.CharList;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrCharListApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

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
        var (service, _, _, gameRoleApiMock, _, _, _, _) = SetupMocks();
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
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
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
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync("CharList_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        // Make image update fail
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
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, characterCacheMock, attachmentStorageMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync("CharList_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<HsrCharacterInformation>>>()))
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

        characterCacheMock.Verify(x => x.UpsertCharacters(Game.HonkaiStarRail, It.IsAny<IEnumerable<string>>()),
            Times.Once);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledForAllAssets()
    {
        // Arrange
        var (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync("CharList_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<IEnumerable<HsrCharacterInformation>>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        // Verify avatar and light cone images were updated
        var characterCount = charList.AvatarList.Count;
        var lightConeCount = charList.AvatarList.Count(x => x.Equip != null);
        var expectedImageCount = characterCount + lightConeCount; // Avatar + Light Cone for each character

        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.Exactly(expectedImageCount));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, userDbContext, _, _) = SetupMocks();
        await using var dbContext = userDbContext;

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        await SeedUserProfileAsync(dbContext, 1ul, 1ul, "test");

        // Force early exit after UpdateGameUid by making char API fail
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: repository should persist updated user with stored game uid
        var storedGameUid = await dbContext.GameUids.FirstOrDefaultAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(storedGameUid, Is.Not.Null);
            Assert.That(storedGameUid!.Game, Is.EqualTo(Game.HonkaiStarRail));
            Assert.That(storedGameUid.Region, Is.EqualTo(Server.Asia.ToString()));
            Assert.That(storedGameUid.GameUid, Is.EqualTo(profile.GameUid));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, userDbContext, _, _) = SetupMocks();
        await using var dbContext = userDbContext;

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        await SeedUserProfileAsync(dbContext, 1ul, 1ul, "test", 1,
            new[]
            {
                new ProfileGameUid
                {
                    ProfileId = 1,
                    Game = Game.HonkaiStarRail,
                    Region = Server.Asia.ToString(),
                    GameUid = profile.GameUid
                }
            });

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: no additional records created
        var entries = await dbContext.GameUids.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].GameUid, Is.EqualTo(profile.GameUid));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, userDbContext, _, _) = SetupMocks();
        await using var dbContext = userDbContext;

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: no persistence when user not found
        Assert.That(await dbContext.GameUids.CountAsync(), Is.EqualTo(0));

        // Case: user exists but no matching profile
        var (service2, characterApiMock2, _, gameRoleApiMock2, _, userDbContext2, _, _) = SetupMocks();
        await using var dbContext2 = userDbContext2;

        gameRoleApiMock2
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        await SeedUserProfileAsync(dbContext2, 1ul, 99999ul, "test");

        characterApiMock2
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(
                Result<IEnumerable<HsrBasicCharacterData>>.Failure(StatusCode.ExternalServerError, "err"));

        await service2.ExecuteAsync(context);
        Assert.That(await dbContext2.GameUids.CountAsync(), Is.EqualTo(0));
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("CharList_TestData_1.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, _, gameRoleApiMock, _, _, attachmentStorageMock) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var charList = await LoadTestDataAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<HsrBasicCharacterData>>.Success([charList]));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test", ("server", Server.Asia.ToString()));

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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        }

        var (service, _, storedAttachments, _) = SetupRealApiIntegrationTest();

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), testLtUid, testLToken!,
            ("server", Server.Asia.ToString()));

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

            // Save output
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, "HsrCharListRealApi.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
        HsrCharListApplicationService Service,
        Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<IEnumerable<HsrCharacterInformation>>> CardServiceMock,
        UserDbContext UserDbContext,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock
        ) SetupMocks()
    {
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<IEnumerable<HsrCharacterInformation>>>();
        var characterApiMock = new Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation,
            CharacterApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrCharListApplicationService>>();
        var userDbContext = m_DbFactory.CreateDbContext<UserDbContext>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new HsrCharListApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            characterApiMock.Object,
            gameRoleApiMock.Object,
            userDbContext,
            characterCacheMock.Object,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, userDbContext,
            characterCacheMock, attachmentStorageMock);
    }

    private (
        HsrCharListApplicationService Service,
        Mock<ICharacterApiService<HsrBasicCharacterData, HsrCharacterInformation, CharacterApiContext>> CharacterApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        UserDbContext UserDbContext,
        Mock<ICharacterCacheService> CharacterCacheMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new HsrCharListCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrCharListCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var characterApiMock = new Mock<ICharacterApiService<HsrBasicCharacterData,
            HsrCharacterInformation, CharacterApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrCharListApplicationService>>();
        var userDbContext = m_DbFactory.CreateDbContext<UserDbContext>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new HsrCharListApplicationService(
            cardService,
            imageUpdaterService,
            characterApiMock.Object,
            gameRoleApiMock.Object,
            userDbContext,
            characterCacheMock.Object,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, characterApiMock, imageUpdaterMock, gameRoleApiMock, userDbContext, characterCacheMock, attachmentStorageMock);
    }

    private (
        HsrCharListApplicationService Service,
        IAttachmentStorageService AttachmentStorageService,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserDbContext
        ) SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new HsrCharListCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrCharListCardService>>(),
            Mock.Of<IApplicationMetrics>());

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

        // Real game role API service
        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        // Real image updater service
        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();

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

        var userDbContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new HsrCharListApplicationService(
            cardService,
            imageUpdaterService,
            characterApiService,
            gameRoleApiService,
            userDbContext,
            characterCacheMock.Object,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<HsrCharListApplicationService>>());

        return (service, attachmentStorageMock.Object, storedAttachments, userDbContext);
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

    private static async Task SeedUserProfileAsync(UserDbContext dbContext, ulong userId, ulong ltUid, string lToken, int profileId = 1, IEnumerable<ProfileGameUid>? gameUids = null)
    {
        var profileGames = gameUids?.ToList() ?? [];
        foreach (var gameUid in profileGames)
        {
            gameUid.ProfileId = profileId;
        }

        var user = new UserModel
        {
            Id = (long)userId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfileModel
                {
                    Id = profileId,
                    UserId = (long)userId,
                    ProfileId = profileId,
                    LtUid = (long)ltUid,
                    LToken = lToken,
                    GameUids = profileGames
                }
            ]
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<HsrBasicCharacterData> LoadTestDataAsync(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<HsrBasicCharacterData>(json);
        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
