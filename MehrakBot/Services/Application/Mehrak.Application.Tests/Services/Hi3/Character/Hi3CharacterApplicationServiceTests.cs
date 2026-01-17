#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hi3.Character;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class Hi3CharacterApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hi3");

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
        var (service, _, _, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 1ul, "bad", ("character", "Kiana"), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        });
    }

    [Test]
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("character", "Kiana"), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character data"));
        }
    }

    [Test]
    public async Task ExecuteAsync_CharacterNotFound_ReturnsEphemeralMessage()
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync("Character_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        var context = CreateContext(1, 1ul, "test", ("character", "NonExistent"), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("NonExistent"));
        }
    }

    [Test]
    public async Task ExecuteAsync_CharacterFoundByAlias_ReturnsSuccess()
    {
        // Arrange
        var (service, characterApiMock, characterCacheMock, aliasServiceMock, gameRoleApiMock, imageUpdaterMock, cardServiceMock, metricsMock, attachmentStorageMock, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var character = await LoadTestCharacterAsync("Character_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        aliasServiceMock.Setup(x => x.GetAliases(Game.HonkaiImpact3))
            .Returns(new Dictionary<string, string> { { "AliasName", character.Avatar.Name } });

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("character", "AliasName"), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
            character.Avatar.Name.ToLowerInvariant()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, imageUpdaterMock, cardServiceMock, _, _, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var character = await LoadTestCharacterAsync("Character_TestData_1.json");
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(new MemoryStream());

        var context = CreateContext(1, 1ul, "test", ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        }
    }

    [Test]
    [TestCase("Character_TestData_1.json")]
    [TestCase("Character_TestData_2.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsCardAndTracksMetrics(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, imageUpdaterMock, cardServiceMock, metricsMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().Any(), Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        metricsMock.Verify(x => x.TrackCharacterSelection(nameof(Game.HonkaiImpact3),
            character.Avatar.Name.ToLowerInvariant()), Times.Once);
    }

    [Test]
    [TestCase("Character_TestData_1.json")]
    public async Task ExecuteAsync_VerifyAllImagesUpdated(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, imageUpdaterMock, cardServiceMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(new MemoryStream());

        var context = CreateContext(1, 1ul, "test", ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var expectedCalls = character.Stigmatas.Count + character.Costumes.Count + 1; // weapon
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.Exactly(expectedCalls));
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, _, _, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "any"), ("server", Hi3Server.SEA.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert
        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
            Assert.That(stored.Region, Is.EqualTo(Hi3Server.SEA.ToString()));
        }
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, _, _, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var seededProfile = SeedUserProfile(userContext, 1ul, 1, 1ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = seededProfile.Id,
            Game = Game.HonkaiImpact3,
            Region = Hi3Server.SEA.ToString(),
            GameUid = profile.GameUid
        });
        await userContext.SaveChangesAsync();

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "any"), ("server", Hi3Server.SEA.ToString()));

        // Act
        await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
            Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
        }
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, characterApiMock, _, _, gameRoleApiMock, _, _, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("character", "any"), ("server", Hi3Server.SEA.ToString()));

        // Act
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);

        SeedUserProfile(userContext, 1ul, 2, 9999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Character_TestData_1.json")]
    [TestCase("Character_TestData_2.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, characterApiMock, gameRoleApiMock, attachmentStorageMock, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var character = await LoadTestCharacterAsync(testDataFile);
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<Hi3CharacterDetail>>.Success(new[] { character }));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test",
            ("character", character.Avatar.Name), ("server", Hi3Server.SEA.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        }

        var attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null);
        Assert.That(!string.IsNullOrWhiteSpace(attachment.FileName));

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment.FileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Explicit("Calls real API - run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        // NOTE: Placeholder for future real API integration if implemented.
        Assert.Pass("Real API integration not implemented for HI3 character list.");
    }

    #endregion

    #region Helper Methods

    private (
          Hi3CharacterApplicationService Service,
          Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>> CharacterApiMock,
          Mock<ICharacterCacheService> CharacterCacheMock,
          Mock<IAliasService> AliasServiceMock,
          Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
         Mock<IImageUpdaterService> ImageUpdaterMock,
         Mock<ICardService<Hi3CharacterDetail>> CardServiceMock,
         Mock<IMetricsService> MetricsServiceMock,
         Mock<IAttachmentStorageService> AttachmentStorageMock,
         UserDbContext UserContext
    ) SetupMocks()
    {
        var characterApiMock = new Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>>();
        var characterCacheMock = new Mock<ICharacterCacheService>();
        var aliasServiceMock = new Mock<IAliasService>();
        aliasServiceMock.Setup(x => x.GetAliases(Game.HonkaiImpact3)).Returns([]);
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<Hi3CharacterDetail>>();
        var metricsMock = new Mock<IMetricsService>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<Hi3CharacterApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<Hi3CharacterDetail>>()))
            .ReturnsAsync(new MemoryStream());

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new Hi3CharacterApplicationService(
            cardServiceMock.Object,
            characterApiMock.Object,
            imageUpdaterMock.Object,
            characterCacheMock.Object,
            aliasServiceMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, characterCacheMock, aliasServiceMock, gameRoleApiMock, imageUpdaterMock, cardServiceMock, metricsMock, attachmentStorageMock, userContext);
    }

    private (
        Hi3CharacterApplicationService Service,
        Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>> CharacterApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
    ) SetupIntegrationTest()
    {
        var characterApiMock = new Mock<ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();

        var cardService = new Hi3CharacterCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<Hi3CharacterCardService>>());

        cardService.InitializeAsync().Wait();

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            CreateHttpClientFactory(),
            Mock.Of<ILogger<ImageUpdaterService>>());

        var characterCacheMock = new Mock<ICharacterCacheService>();

        var aliasServiceMock = new Mock<IAliasService>();
        aliasServiceMock.Setup(x => x.GetAliases(Game.HonkaiImpact3)).Returns([]);

        var metricsMock = new Mock<IMetricsService>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<Hi3CharacterApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new Hi3CharacterApplicationService(
            cardService,
            characterApiMock.Object,
            imageUpdaterService,
            characterCacheMock.Object,
            aliasServiceMock.Object,
            metricsMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, characterApiMock, gameRoleApiMock, attachmentStorageMock, userContext);
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

    private static IHttpClientFactory CreateHttpClientFactory()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return factoryMock.Object;
    }

    private static async Task<Hi3CharacterDetail> LoadTestCharacterAsync(string file)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var path = Path.Combine(TestDataPath, file);
        var json = await File.ReadAllTextAsync(path);
        var data = JsonSerializer.Deserialize<Hi3CharacterDetail>(json, options);
        return data ?? throw new InvalidOperationException($"Failed to deserialize {file}");
    }

    private static GameProfileDto CreateTestProfile() => new()
    {
        GameUid = "800000000",
        Nickname = "TestPlayer",
        Level = 88
    };

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
