using System.Text.Json;
using Mehrak.Application.Hsr.Anomaly;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
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
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Hsr.Anomaly;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrAnomalyApplicationServiceTests
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
    public async Task ExecuteAsync_ApiError_ReturnsApiError(StatusCode status)
    {
        // Arrange
        var (service, apiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Failure(status, "unsafe upstream text"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(status == StatusCode.ExternalServerError ? CommandFailureReason.ApiError : CommandFailureReason.Timeout));
            Assert.That(result.Error, Is.EqualTo(status == StatusCode.ExternalServerError ? new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.AnomalyArbitration) : new CommandError(CommandErrorCode.Timeout, CommandErrorTarget.None)));
            Assert.That(result.Error!.Message, Does.Not.Contain("unsafe upstream text"));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoRecords_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, apiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = new HsrAnomalyInformation
        {
            ChallengeRecords = [],
            BestRecord = new RecordBrief
            {
                RankIcon = ""
            }
        };

        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

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

    [TestCase(RankIconType.ChallengePeakRankIconTypeNone)]
    [TestCase(RankIconType.ChallengePeakRankIconTypeBronze)]
    public async Task ExecuteAsync_NoMatchingBestRecord_ReturnsNoClearRecords(RankIconType rank)
    {
        // Arrange
        var (service, apiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = new HsrAnomalyInformation
        {
            ChallengeRecords = [
                new ChallengeRecord
                {
                    HasChallengeRecord = true,
                    BossStars = 0,
                    MobStars = 0,
                    Group = new AnomalyGroup
                    {
                        GameVersion = "1.0",
                        BeginTime = new ScheduleTime(),
                        EndTime = new ScheduleTime(),
                        Name = "",
                        ThemePicPath = ""
                    },
                    BossInfo = new BossInfo { Icon = "", MazeId = 0, Name = "" },
                    MobInfo = [],
                    MobRecords = [],
                    BattleNum = 0
                }
            ],
            BestRecord = new RecordBrief
            {
                RankIconType = rank,
                BossStars = 1,
                MobStars = 1,
                RankIcon = ""
            }
        };

        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

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
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsBotError()
    {
        // Arrange
        var (service, apiMock, imageUpdaterMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = await LoadTestDataAsync("Anomaly_TestData_1.json");
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image)));
        });
    }

    [Test]
    [TestCase("Anomaly_TestData_1.json", Server.Asia)]
    [TestCase("Anomaly_TestData_1.json", Server.Europe)]
    [TestCase("Anomaly_TestData_1.json", Server.America)]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(string testDataFile, Server server)
    {
        // Arrange
        var (service, apiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = await LoadTestDataAsync(testDataFile);
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrAnomalyInformation>>()))
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
            Assert.That(GetOutcome(result).Mode, Is.EqualTo(EndgameMode.AnomalyArbitration));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        var offset = server.GetTimeZoneInfo().BaseUtcOffset;
        var selected = anomalyData.BestRecord.RankIconType != RankIconType.ChallengePeakRankIconTypeNone
            ? anomalyData.ChallengeRecords.First(x => x.HasChallengeRecord && x.BossStars == anomalyData.BestRecord.BossStars && x.MobStars == anomalyData.BestRecord.MobStars)
            : anomalyData.ChallengeRecords.First(x => x.HasChallengeRecord && x.MobStars == anomalyData.BestRecord.MobStars);
        Assert.That(GetOutcome(result).CycleStart, Is.EqualTo(new DateTimeOffset(selected.Group.BeginTime.ToDateTime(), offset).ToUniversalTime()));
        Assert.That(GetOutcome(result).CycleEnd, Is.EqualTo(new DateTimeOffset(selected.Group.EndTime.ToDateTime(), offset).ToUniversalTime()));

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
    [TestCase("Anomaly_TestData_1.json", Server.Asia)]
    public async Task ExecuteAsync_AttachmentStorageFailure_ReturnsSafeCodedError(string testDataFile, Server server)
    {
        // Arrange
        var (service, apiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = await LoadTestDataAsync(testDataFile);
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrAnomalyInformation>>()))
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
    [TestCase("Anomaly_TestData_1.json")]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly(string testDataFile)
    {
        // Arrange
        var (service, apiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = await LoadTestDataAsync(testDataFile);
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrAnomalyInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar images were updated
        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, apiMock, _, gameRoleApiMock, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        apiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Failure(StatusCode.ExternalServerError, "err"));

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

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Anomaly_TestData_1.json")]
    [TestCase("Anomaly_TestData_2.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, apiMock, imageUpdaterMock, gameRoleApiMock, attachmentStorageMock, storedAttachments, _) = await SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var anomalyData = await LoadTestDataAsync(testDataFile);
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Success(anomalyData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.Error?.Message}");
            Assert.That(result.Outcome, Is.TypeOf<EndgameOutcome>());
        });
        Assert.That(GetOutcome(result).State, Is.EqualTo(OperationOutcome.Complete));

        var attachment = GetOutcome(result).Image;
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(!string.IsNullOrWhiteSpace(attachment!.StorageFileName));

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment!.StorageFileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        if (storedAttachments.TryGetValue(attachment!.StorageFileName, out var stored))
        {
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(
                outputDirectory,
                $"HsrAnomalyIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

            stored.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await stored.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
          HsrAnomalyApplicationService Service,
          Mock<IApiService<HsrAnomalyInformation, BaseHoYoApiContext>> ApiMock,
          Mock<IImageUpdaterService> ImageUpdaterMock,
          Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
          Mock<ICardService<HsrAnomalyInformation>> CardServiceMock,
          Mock<IAttachmentStorageService> AttachmentStorageMock,
          UserDbContext UserContext
          ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrAnomalyInformation>>();
        var apiMock = new Mock<IApiService<HsrAnomalyInformation, BaseHoYoApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrAnomalyApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new HsrAnomalyApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            apiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, apiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, attachmentStorageMock, userContext);
    }

    private async Task<(
         HsrAnomalyApplicationService Service,
         Mock<IApiService<HsrAnomalyInformation, BaseHoYoApiContext>> ApiMock,
         Mock<IImageUpdaterService> ImageUpdaterMock,
         Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
         Mock<IAttachmentStorageService> AttachmentStorageMock,
         Dictionary<string, MemoryStream> StoredAttachments,
         UserDbContext UserContext
         )> SetupIntegrationTest()
    {
        var cardService = new HsrAnomalyCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrAnomalyCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var apiMock = new Mock<IApiService<HsrAnomalyInformation, BaseHoYoApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrAnomalyApplicationService>>();
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

        await cardService.InitializeAsync();

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new HsrAnomalyApplicationService(
            cardService,
            imageUpdaterService,
            apiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, apiMock, imageUpdaterMock, gameRoleApiMock, attachmentStorageMock, storedAttachments, userContext);
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

    private static async Task<HsrAnomalyInformation> LoadTestDataAsync(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<HsrAnomalyInformation>(json);

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
    [TestCase(RankIconType.ChallengePeakRankIconTypeNone, 1)]
    [TestCase(RankIconType.ChallengePeakRankIconTypeBronze, 2)]
    public async Task ExecuteAsync_SelectedRecordSuppliesCycleAndIgnoresUnclearedAndLaterMatches(RankIconType rank, int selectedIndex)
    {
        var (service, api, updater, profile, card, storage, _) = SetupMocks();
        profile.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>())).ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        ChallengeRecord Record(int index, bool hasRecord, int bossStars) => new()
        {
            HasChallengeRecord = hasRecord, BossStars = bossStars, MobStars = 3,
            Group = new AnomalyGroup
            {
                BeginTime = new ScheduleTime { Year = 2025, Month = 11, Day = 10 + index, Hour = 4 },
                EndTime = new ScheduleTime { Year = 2025, Month = 11, Day = 20 + index, Hour = 4 },
                Name = "", ThemePicPath = "", GameVersion = "3.7"
            },
            BossInfo = new BossInfo { Icon = "", Name = "", MazeId = 0 }, MobInfo = [], MobRecords = []
        };
        var data = new HsrAnomalyInformation
        {
            BestRecord = new RecordBrief { RankIcon = "", RankIconType = rank, BossStars = 3, MobStars = 3 },
            ChallengeRecords = [Record(0, false, 3), Record(1, true, 0), Record(2, true, 3), Record(3, true, 3)]
        };
        api.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>())).ReturnsAsync(Result<HsrAnomalyInformation>.Success(data));
        storage.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await service.ExecuteAsync(CreateContext(1, 1, "test", ("server", Server.Asia)));
        Assert.That(result.IsSuccess, Is.True);
        var outcome = GetOutcome(result);
        Assert.That(outcome.CycleStart, Is.EqualTo(new DateTimeOffset(2025, 11, 10 + selectedIndex, 4, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime()));
        Assert.That(outcome.CycleEnd, Is.EqualTo(new DateTimeOffset(2025, 11, 20 + selectedIndex, 4, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime()));
        updater.Verify(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()), Times.Never);
        card.Verify(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrAnomalyInformation>>()), Times.Never);
    }
    private static EndgameOutcome GetOutcome(CommandResult result)
    {
        Assert.That(result.Outcome, Is.TypeOf<EndgameOutcome>());
        var outcome = (EndgameOutcome)result.Outcome!;
        Assert.That(outcome.Game, Is.EqualTo(Game.HonkaiStarRail));
        Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.AnomalyArbitration));
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
