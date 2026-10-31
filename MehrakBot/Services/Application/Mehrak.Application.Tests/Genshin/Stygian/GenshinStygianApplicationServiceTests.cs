#region

using System.Text.Json;
using Mehrak.Application.Genshin.Stygian;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
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
using Mehrak.GameApi.Genshin;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Genshin.Stygian;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinStygianApplicationServiceTests
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
        var (service, _, gameRoleApiMock, _, _, _) = SetupMocks();
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
    public async Task ExecuteAsync_StygianApiError_ReturnsApiError(StatusCode status)
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(status, "unsafe upstream text"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(status == StatusCode.ExternalServerError ? CommandFailureReason.ApiError : CommandFailureReason.Timeout));
            Assert.That(result.Error, Is.EqualTo(status == StatusCode.ExternalServerError ? new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.StygianOnslaught) : new CommandError(CommandErrorCode.Timeout, CommandErrorTarget.None)));
            Assert.That(result.Error!.Message, Does.Not.Contain("unsafe upstream text"));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_StygianNotUnlocked_ReturnsLockedOutcome()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = new GenshinStygianInformation
        {
            IsUnlock = false,
            Data = null
        };

        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            AssertEmptyOutcome(result, OperationOutcome.NotUnlocked);
        }
    }

    [Test]
    public async Task ExecuteAsync_NoStygianData_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = new GenshinStygianInformation
        {
            IsUnlock = true,
            Data =
            [
                new StygianData
                {
                    Single = new StygianChallengeData
                    {
                        HasData = false,
                        Challenge = null,
                        StygianBestRecord = null
                    },
                    Multi = new StygianChallengeData
                    {
                        HasData = false,
                        Challenge = null,
                        StygianBestRecord = null
                    }
                }
            ]
        };

        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

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
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

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
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("server", server.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(GetOutcome(result).State, Is.EqualTo(OperationOutcome.Complete));
            Assert.That(GetOutcome(result).Image, Is.Not.Null);
            Assert.That(GetOutcome(result).Mode, Is.EqualTo(EndgameMode.StygianOnslaught));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(GetOutcome(result).CycleStart, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianData.Data![0].Schedule!.StartTime!))));
        Assert.That(GetOutcome(result).CycleEnd, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianData.Data[0].Schedule!.EndTime!))));

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
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
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
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync("Stygian_TestData_1.json");
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar images, side avatar images, and monster images were updated
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(d => d != null),
                It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

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

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
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

        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
            Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
        }
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, _, attachmentStorageMock, userContext) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        stygianApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);

        SeedUserProfile(userContext, 1ul, 2, 99999ul);
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Stygian_TestData_1.json")]
    [TestCase("Stygian_TestData_2.json")]
    [TestCase("Stygian_TestData_3.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, storedAttachments, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var stygianData = await LoadTestDataAsync(testDataFile);
        stygianApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinStygianInformation>.Success(stygianData));

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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(!string.IsNullOrWhiteSpace(attachment!.StorageFileName));
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.Is<string>(n => n == attachment!.StorageFileName), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        if (storedAttachments.TryGetValue(attachment!.StorageFileName, out var stored))
        {
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory,
                $"StygianIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        }

        var (service, storedAttachments, _) = SetupRealApiIntegrationTest();

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

            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, "StygianRealApi.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
        GenshinStygianApplicationService Service,
        Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> StygianApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupMocks()
    {
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var cardServiceMock = new Mock<ICardService<StygianData>>();
        var stygianApiMock = new Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<GenshinStygianApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<StygianData>>()))
            .ReturnsAsync(cardStream);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinStygianApplicationService(
            imageUpdaterMock.Object,
            cardServiceMock.Object,
            stygianApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, userContext);
    }

    private (
        GenshinStygianApplicationService Service,
        Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>> StygianApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var cardService = new GenshinStygianCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var stygianApiMock = new Mock<IApiService<GenshinStygianInformation, BaseHoYoApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinStygianApplicationService>>();
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

        cardService.InitializeAsync().Wait();

        var service = new GenshinStygianApplicationService(
            imageUpdaterService,
            cardService,
            stygianApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, stygianApiMock, gameRoleApiMock, imageUpdaterMock, attachmentStorageMock, storedAttachments, userContext);
    }

    private (GenshinStygianApplicationService Service, Dictionary<string, MemoryStream> StoredAttachments, UserDbContext UserContext) SetupRealApiIntegrationTest()
    {
        var cardService = new GenshinStygianCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinStygianCardService>>(),
            Mock.Of<IApplicationMetrics>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var stygianApi = new GenshinStygianApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GenshinStygianApiService>>());

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

        var service = new GenshinStygianApplicationService(
            imageUpdaterService,
            cardService,
            stygianApi,
            gameRoleApiService,
            userContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<GenshinStygianApplicationService>>());

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

    private static async Task<GenshinStygianInformation> LoadTestDataAsync(string filename)
    {
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var data = JsonSerializer.Deserialize<StygianData>(json);

        Assert.That(data, Is.Not.Null);

        GenshinStygianInformation result = new()
        {
            IsUnlock = true,
            Data = [data]
        };

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
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_UsesFirstSingleRecordRatherThanMultiOrLaterRecords(bool firstHasData)
    {
        var (service, api, profile, updater, storage, _) = SetupMocks();
        profile.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>())).ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        StygianData Record(bool hasData, string start, string end) => new()
        {
            Schedule = new StygianSchedule { ScheduleId = "1", StartTime = start, EndTime = end, Name = "" },
            Single = new StygianChallengeData { HasData = hasData },
            Multi = new StygianChallengeData { HasData = true }
        };
        var data = new GenshinStygianInformation
        {
            IsUnlock = true,
            Data = [Record(firstHasData, "1763300000", "1764300000"), Record(true, "1765300000", "1766300000")]
        };
        api.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>())).ReturnsAsync(Result<GenshinStygianInformation>.Success(data));
        storage.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await service.ExecuteAsync(CreateContext(1, 1, "test", ("server", Server.Asia)));
        Assert.That(result.IsSuccess, Is.True);
        if (firstHasData)
        {
            Assert.That(GetOutcome(result).CycleStart, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1763300000)));
            Assert.That(GetOutcome(result).CycleEnd, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1764300000)));
        }
        else
        {
            AssertEmptyOutcome(result, OperationOutcome.Empty);
            storage.Verify(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        updater.Verify(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()), Times.Never);
    }
    private static EndgameOutcome GetOutcome(CommandResult result)
    {
        Assert.That(result.Outcome, Is.TypeOf<EndgameOutcome>());
        var outcome = (EndgameOutcome)result.Outcome!;
        Assert.That(outcome.Game, Is.EqualTo(Game.Genshin));
        Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.StygianOnslaught));
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
