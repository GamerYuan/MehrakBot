using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Hsr.Anomaly;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Services.Hsr.Anomaly;

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
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_ApiError_ReturnsApiError()
    {
        // Arrange
        var (service, apiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrAnomalyInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Anomaly Arbitration data"));
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
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoMatchingBestRecord_ReturnsNoClearRecords()
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
                RankIconType = RankIconType.ChallengePeakRankIconTypeBronze,
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
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
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
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        });
    }

    [Test]
    [TestCase("Anomaly_TestData_1.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(string testDataFile)
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

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>()
                    .Any(x => x.Content.Contains("Anomaly Arbitration Summary")),
                Is.True);
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
            Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
            Assert.That(result.Data, Is.Not.Null);
        });
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
            Mock.Of<ILogger<HsrAnomalyCardService>>());

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
}
