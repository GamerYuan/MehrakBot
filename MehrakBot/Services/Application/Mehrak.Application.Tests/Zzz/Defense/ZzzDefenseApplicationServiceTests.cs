#region

using System.Text.Json;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Application.Zzz.Defense;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Card;
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
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Zzz.Defense;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzDefenseApplicationServiceTests
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
        var (service, _, _, gameRoleApiMock, _, _, _) = SetupMocks();
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
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.AuthenticationRequired, CommandErrorTarget.GameProfile)));
        }
    }

    [TestCase(StatusCode.ExternalServerError)]
    [TestCase(StatusCode.Timeout)]
    [TestCase(StatusCode.Cancelled)]
    public async Task ExecuteAsync_DefenseApiError_ReturnsApiError(StatusCode status)
    {
        // Arrange
        var (service, defenseApiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Failure(status, "unsafe upstream text"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(status == StatusCode.ExternalServerError ? CommandFailureReason.ApiError : CommandFailureReason.Timeout));
            Assert.That(result.Error, Is.EqualTo(status == StatusCode.ExternalServerError ? new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.ShiyuDefense) : new CommandError(CommandErrorCode.Timeout, CommandErrorTarget.None)));
            Assert.That(result.Error!.Message, Does.Not.Contain("unsafe upstream text"));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoData_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, defenseApiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = new ZzzDefenseDataV2
        {
            PassFifthFloor = false,
            Brief = null,
            FifthLayerDetail = null,
            HadalBeginTime = new ScheduleTime(),
            HadalEndTime = new ScheduleTime(),
            BeginTime = "1704067200",
            EndTime = "1704672000",
        };

        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

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

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ExecuteAsync_NoValidFloors_ReturnsNoClearRecords(int detailState)
    {
        // Arrange
        var (service, defenseApiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = new ZzzDefenseDataV2
        {
            PassFifthFloor = false,
            Brief = new HadalBrief { Rating = "", ChallengeTime = new ScheduleTime() },
            FifthLayerDetail = detailState == 0 ? null : new HadalFifthFloorDetail { LayerChallengeInfoList = detailState == 1 ? null! : [] },
            HadalBeginTime = new ScheduleTime(),
            HadalEndTime = new ScheduleTime(),
            BeginTime = "1704067200",
            EndTime = "1704672000",
        };

        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

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
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, defenseApiMock, imageUpdaterMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = await LoadTestDataAsync("Shiyu_TestData_1.json");
        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

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

    [Test]
    [TestCase("Shiyu_TestData_1.json", Server.Asia)]
    [TestCase("Shiyu_TestData_1.json", Server.Europe)]
    [TestCase("Shiyu_TestData_1.json", Server.America)]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(string testDataFile, Server server)
    {
        // Arrange
        var (service, defenseApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, attachmentStorageMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = await LoadTestDataAsync(testDataFile);
        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzDefenseDataV2>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("server", server.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(GetOutcome(result).State, Is.EqualTo(OperationOutcome.Complete));
            Assert.That(GetOutcome(result).Image, Is.Not.Null);
            Assert.That(GetOutcome(result).Mode, Is.EqualTo(EndgameMode.ShiyuDefense));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(GetOutcome(result).CycleStart, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(long.Parse(defenseData.BeginTime))));
        Assert.That(GetOutcome(result).CycleEnd, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(long.Parse(defenseData.EndTime))));

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

    [Test]
    [TestCase("Shiyu_TestData_1.json", Server.Asia)]
    public async Task ExecuteAsync_AttachmentStorageFailure_ReturnsSafeCodedError(string testDataFile, Server server)
    {
        // Arrange
        var (service, defenseApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, attachmentStorageMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = await LoadTestDataAsync(testDataFile);
        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzDefenseDataV2>>()))
            .ReturnsAsync(cardStream);

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
    [TestCase("Shiyu_TestData_1.json")]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly(string testDataFile)
    {
        // Arrange
        var (service, defenseApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = await LoadTestDataAsync(testDataFile);
        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzDefenseDataV2>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar and buddy images were updated
        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, defenseApiMock, _, gameRoleApiMock, _, userContext, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        defenseApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Failure(StatusCode.ExternalServerError, "err"));

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
        var (service, defenseApiMock, _, gameRoleApiMock, _, userContext, _) = SetupMocks();

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

        defenseApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Failure(StatusCode.ExternalServerError, "err"));

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
        var (service, defenseApiMock, _, gameRoleApiMock, _, userContext, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        defenseApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        SeedUserProfile(userContext, 1ul, 2, 99999ul);

        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Shiyu_TestData_1.json")]
    [TestCase("Shiyu_TestData_2.json")]
    [TestCase("Shiyu_TestData_3.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, defenseApiMock, _, gameRoleApiMock, attachmentStorageMock, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var defenseData = await LoadTestDataAsync(testDataFile);
        defenseApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzDefenseDataV2>.Success(defenseData));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.Error?.Message}");
            Assert.That(result.Outcome, Is.TypeOf<EndgameOutcome>());
        }
        Assert.That(GetOutcome(result).State, Is.EqualTo(OperationOutcome.Complete));

        var attachment = GetOutcome(result).Image;
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(!string.IsNullOrWhiteSpace(attachment!.StorageFileName));

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment!.StorageFileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
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
        Assert.That(result.IsSuccess, Is.True, $"API call failed: {result.Error?.Message}");

        if (result.IsSuccess)
        {
            var attachment = GetOutcome(result).Image;
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(storedAttachments.TryGetValue(attachment!.StorageFileName, out var storedStream), Is.True);
            Assert.That(storedStream!.Length, Is.GreaterThan(0));

            // Save output
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, "ZzzDefenseRealApi.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
        ZzzDefenseApplicationService Service,
        Mock<IApiService<ZzzDefenseDataV2, BaseHoYoApiContext>> DefenseApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<ZzzDefenseDataV2>> CardServiceMock,
        UserDbContext UserContext,
        Mock<IAttachmentStorageService> AttachmentStorageMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<ZzzDefenseDataV2>>();
        var defenseApiMock = new Mock<IApiService<ZzzDefenseDataV2, BaseHoYoApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<ZzzDefenseApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new ZzzDefenseApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            defenseApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, defenseApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, userContext, attachmentStorageMock);
    }

    private (
        ZzzDefenseApplicationService Service,
        Mock<IApiService<ZzzDefenseDataV2, BaseHoYoApiContext>> DefenseApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var cardService = new ZzzDefenseCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzDefenseCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var defenseApiMock = new Mock<IApiService<ZzzDefenseDataV2, BaseHoYoApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<ZzzDefenseApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        cardService.InitializeAsync().Wait();

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new ZzzDefenseApplicationService(
            cardService,
            imageUpdaterService,
            defenseApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, defenseApiMock, imageUpdaterMock, gameRoleApiMock, attachmentStorageMock, userContext);
    }

    private (
        ZzzDefenseApplicationService Service,
        IAttachmentStorageService AttachmentStorageService,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupRealApiIntegrationTest()
    {
        var cardService = new ZzzDefenseCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzDefenseCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var defenseApiService = new ZzzDefenseApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<ZzzDefenseApiService>>());

        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ICacheService>(),
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

        var service = new ZzzDefenseApplicationService(
            cardService,
            imageUpdaterService,
            defenseApiService,
            gameRoleApiService,
            userContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<ZzzDefenseApplicationService>>());

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
            GameUid = "1000000000",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static async Task<ZzzDefenseDataV2> LoadTestDataAsync(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<ZzzDefenseDataV2>(json);

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
    private static EndgameOutcome GetOutcome(CommandResult result)
    {
        Assert.That(result.Outcome, Is.TypeOf<EndgameOutcome>());
        var outcome = (EndgameOutcome)result.Outcome!;
        Assert.That(outcome.Game, Is.EqualTo(Game.ZenlessZoneZero));
        Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.ShiyuDefense));
        Assert.That(outcome.GameUid, Is.Not.Empty);
        Assert.That(result.ToProto().ToDomain().Outcome, Is.EqualTo(outcome));
        if (outcome.State == OperationOutcome.Complete)
        {
            Assert.That(outcome.CycleStart, Is.Not.Null);
            Assert.That(outcome.CycleEnd, Is.Not.Null);
            Assert.That(outcome.CycleStart!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(outcome.CycleEnd!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
        }
        if (outcome.State == OperationOutcome.Complete)
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
