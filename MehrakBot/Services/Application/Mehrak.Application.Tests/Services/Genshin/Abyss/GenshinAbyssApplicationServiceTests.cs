#region

using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
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

namespace Mehrak.Application.Tests.Services.Genshin.Abyss;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinAbyssApplicationServiceTests
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
        var (service, _, gameRoleApiMock, _, _, _, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_AbyssApiError_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Spiral Abyss data"));
        }
    }

    [Test]
    public async Task ExecuteAsync_FloorNotFound_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = new GenshinAbyssInformation
        {
            Floors =
            [
                new()
                {
                    Index = 11,
                    Levels = []
                }
            ],
            RevealRank = [],
            DefeatRank = [],
            DamageRank = [],
            TakeDamageRank = [],
            NormalSkillRank = [],
            EnergySkillRank = []
        };

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, characterApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "Character API Error"));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Character List"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock, attachmentStorageMock, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinAbyssInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(
                result.Data.Components.OfType<CommandText>().Any(x => x.Content.Contains("Spiral Abyss Summary")),
                Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock, _, _) =
            SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinAbyssInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert Verify avatar images were updated (for battles + rank avatars)
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(d => d != null),
                It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert persisted
        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
        Assert.That(stored.Region, Is.EqualTo(Server.Asia.ToString()));
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, userContext) = SetupMocks();

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
        userContext.ChangeTracker.Clear();

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert not persisted additionally
            Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
            Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _, _, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("floor", 12u), ("server", Server.Asia.ToString()));

        // Act - user missing
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        // Case: user present but no matching profile
        SeedUserProfile(userContext, 1ul, 2, 99999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Abyss_TestData_1.json", 12u)]
    [TestCase("Abyss_TestData_2.json", 12u)]
    [TestCase("Abyss_TestData_3.json", 12u)]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile, uint floor)
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, _, attachmentStorageMock, storedAttachments, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>(testDataFile);
        abyssApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock.Setup(x => x.GetAllCharactersAsync(It.IsAny<GenshinCharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test",
            ("floor", floor), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        }
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));

        var attachment = result.Data.Components.OfType<CommandAttachment>().FirstOrDefault();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(!string.IsNullOrWhiteSpace(attachment!.FileName));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment.FileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        if (storedAttachments.TryGetValue(attachment!.FileName, out var stored))
        {
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory,
                $"AbyssIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}_Floor{floor}.jpg");

            stored.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await stored.CopyToAsync(fileStream);
        }
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        var testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        var testLToken = config["LToken"];
        const uint floor = 12u;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        }

        var (service, storedAttachments, _) = SetupRealApiIntegrationTest();

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), testLtUid, testLToken!,
            ("floor", floor), ("server", Server.Asia.ToString()));

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
            var outputImagePath = Path.Combine(outputDirectory, $"AbyssRealApi_Floor{floor}.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
        GenshinAbyssApplicationService Service,
        Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>> AbyssApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        Mock<ICardService<GenshinAbyssInformation>>
        CardServiceMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var cardServiceMock =
            new Mock<ICardService<GenshinAbyssInformation>>();
        var abyssApiMock = new Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>>();
        var characterApiMock =
            new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinAbyssApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinAbyssApplicationService(
            cardServiceMock.Object,
            abyssApiMock.Object,
            characterApiMock.Object,
            imageUpdaterMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock,
            attachmentStorageMock, userContext);
    }

    private (
        GenshinAbyssApplicationService Service,
        Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>> AbyssApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>
        CharacterApiMock,
        ICardService<GenshinAbyssInformation> CardService,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var cardService = new GenshinAbyssCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinAbyssCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());

        var abyssApiMock = new Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>>();
        var characterApiMock =
            new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, GenshinCharacterApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinAbyssApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var storedAttachments = new Dictionary<string, MemoryStream>();
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

        cardService.InitializeAsync().Wait();

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinAbyssApplicationService(
            cardService,
            abyssApiMock.Object,
            characterApiMock.Object,
            imageUpdaterService,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock,
            characterApiMock, cardService, attachmentStorageMock, storedAttachments, userContext);
    }

    private (
        GenshinAbyssApplicationService Service,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupRealApiIntegrationTest()
    {
        var cardService = new GenshinAbyssCardService(S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinAbyssCardService>>(),
            Mock.Of<Mehrak.Application.Services.Abstractions.IApplicationMetrics>());

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

        var abyssApi = new GenshinAbyssApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinAbyssApiService>>());

        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

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

        var service = new GenshinAbyssApplicationService(
            cardService,
            abyssApi,
            characterApiService,
            imageUpdaterService,
            gameRoleApiService,
            userContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<GenshinAbyssApplicationService>>());

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
                Id = 10000032,
                Icon = "",
                Name = "Bennett",
                Element = "Pyro",
                Level = 80,
                Rarity = 4,
                ActivedConstellationNum = 6,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000037,
                Icon = "",
                Name = "Ganyu",
                Element = "Cryo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 1,
                Weapon = new Weapon { Icon = "", Name = "Bow" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000063,
                Icon = "",
                Name = "Shenhe",
                Element = "Cryo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 2,
                Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000089,
                Icon = "",
                Name = "Mika",
                Element = "Cryo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 6,
                Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000103,
                Icon = "",
                Name = "Furina",
                Element = "Hydro",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 3,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000106,
                Icon = "",
                Name = "Clorinde",
                Element = "Electro",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 0,
                Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000107,
                Icon = "",
                Name = "Emilie",
                Element = "Dendro",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 4,
                Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new GenshinBasicCharacterData
            {
                Id = 10000112,
                Icon = "",
                Name = "Chasca",
                Element = "Anemo",
                Level = 90,
                Rarity = 5,
                ActivedConstellationNum = 5,
                Weapon = new Weapon { Icon = "", Name = "Bow" }
            }
        ];
    }

    private static async Task<T> LoadTestDataAsync<T>(string filename) where T : class
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
        userContext.ChangeTracker.Clear();
        return profile;
    }
    #endregion

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
}
