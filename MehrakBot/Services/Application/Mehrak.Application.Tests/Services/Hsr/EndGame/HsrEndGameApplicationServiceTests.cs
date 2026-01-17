#region

using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Services.Hsr.EndGame;
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

namespace Mehrak.Application.Tests.Services.Hsr.EndGame;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrEndGameApplicationServiceTests
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

        var context = CreateContext(1, 1ul, "test", ("mode", HsrEndGameMode.PureFiction), ("server", Server.Asia.ToString()));

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
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    public async Task ExecuteAsync_EndGameApiError_ReturnsApiError(HsrEndGameMode mode)
    {
        // Arrange
        var (service, endGameApiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = CreateContext(1, 1ul, "test", ("mode", mode), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain($"{mode.GetString()} data"));
        }
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    public async Task ExecuteAsync_NoData_ReturnsNoClearRecords(HsrEndGameMode mode)
    {
        // Arrange
        var (service, endGameApiMock, _, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var endGameData = new HsrEndInformation
        {
            HasData = false,
            Groups = [],
            AllFloorDetail = [],
            MaxFloor = "0",
            StarNum = 0,
            BattleNum = 0,
            MaxFloorId = 0
        };

        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        var context = CreateContext(1, 1ul, "test", ("mode", mode), ("server", Server.Asia.ToString()));

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
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError(HsrEndGameMode mode)
    {
        // Arrange
        var (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var testDataFile = mode == HsrEndGameMode.PureFiction ? "Pf_TestData_1.json" : "As_TestData_1.json";
        var endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = CreateContext(1, 1ul, "test", ("mode", mode), ("server", Server.Asia.ToString()));

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
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_1.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_1.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(HsrEndGameMode mode, string testDataFile)
    {
        // Arrange
        var (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, attachmentStorageMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrEndInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("mode", mode), ("server", Server.Asia.ToString()));

        // Act
        var result = await service.ExecuteAsync(context);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>()
                    .Any(x => x.Content.Contains($"{mode.GetString()} Summary")),
                Is.True);
        }

        attachmentStorageMock.Verify(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_1.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_1.json")]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly(HsrEndGameMode mode, string testDataFile)
    {
        // Arrange
        var (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, _, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrEndInformation>>()))
            .ReturnsAsync(cardStream);

        var context = CreateContext(1, 1ul, "test", ("mode", mode), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar images and buff images were updated
        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_StoresGameUid_WhenNotPreviouslyStored()
    {
        // Arrange
        var (service, endGameApiMock, _, gameRoleApiMock, _, userContext, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        SeedUserProfile(userContext, 1ul, 1, 1ul);

        endGameApiMock
            .Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("mode", HsrEndGameMode.PureFiction), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        var stored = await userContext.GameUids.SingleOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.GameUid, Is.EqualTo(profile.GameUid));
            Assert.That(stored.Region, Is.EqualTo(Server.Asia.ToString()));
            Assert.That(stored.Game, Is.EqualTo(Game.HonkaiStarRail));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenAlreadyStored()
    {
        // Arrange
        var (service, endGameApiMock, _, gameRoleApiMock, _, userContext, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        var seededProfile = SeedUserProfile(userContext, 1ul, 1, 1ul);
        userContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = seededProfile.Id,
            Game = Game.HonkaiStarRail,
            Region = Server.Asia.ToString(),
            GameUid = profile.GameUid
        });
        await userContext.SaveChangesAsync();

        endGameApiMock
            .Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("mode", HsrEndGameMode.PureFiction), ("server", Server.Asia.ToString()));

        // Act
        await service.ExecuteAsync(context);

        Assert.That(await userContext.GameUids.CountAsync(), Is.EqualTo(1));
        Assert.That((await userContext.GameUids.SingleAsync()).GameUid, Is.EqualTo(profile.GameUid));
    }

    [Test]
    public async Task ExecuteAsync_DoesNotStoreGameUid_WhenUserOrProfileMissing()
    {
        // Arrange
        var (service, endGameApiMock, _, gameRoleApiMock, _, userContext, _) = SetupMocks();

        var profile = CreateTestProfile();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(profile));

        endGameApiMock
            .Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError, "err"));

        var context = CreateContext(1, 1ul, "test", ("mode", HsrEndGameMode.PureFiction), ("server", Server.Asia.ToString()));

        // Case: user not found
        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);

        // Case: user exists but no matching profile
        SeedUserProfile(userContext, 1ul, 2, 99999ul);

        await service.ExecuteAsync(context);
        Assert.That(await userContext.GameUids.AnyAsync(), Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_1.json")]
    [TestCase(HsrEndGameMode.PureFiction, "Pf_TestData_2.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_1.json")]
    [TestCase(HsrEndGameMode.ApocalypticShadow, "As_TestData_2.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(HsrEndGameMode mode, string testDataFile)
    {
        // Arrange
        var (service, endGameApiMock, _, gameRoleApiMock, attachmentStorageMock, _) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var endGameData = await LoadTestDataAsync(testDataFile);
        endGameApiMock.Setup(x => x.GetAsync(It.IsAny<HsrEndGameApiContext>()))
            .ReturnsAsync(Result<HsrEndInformation>.Success(endGameData));

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), 1ul, "test", ("mode", mode), ("server", Server.Asia.ToString()));

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
    [TestCase(HsrEndGameMode.PureFiction)]
    [TestCase(HsrEndGameMode.ApocalypticShadow)]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow(HsrEndGameMode mode)
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

        var (service, _, storedAttachments, _) = SetupRealApiIntegrationTest();

        var context = CreateContext(S3TestHelper.Instance.GetUniqueUserId(), testLtUid, testLToken!,
            ("mode", mode), ("server", Server.Asia.ToString()));

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

            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            var outputImagePath = Path.Combine(outputDirectory, $"HsrEndGameRealApi_{mode}.jpg");

            storedStream.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await storedStream.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private (
        HsrEndGameApplicationService Service,
        Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>> EndGameApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<HsrEndInformation>> CardServiceMock,
        UserDbContext UserContext,
        Mock<IAttachmentStorageService> AttachmentStorageMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrEndInformation>>();
        var endGameApiMock = new Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrEndGameApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new HsrEndGameApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            endGameApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        return (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock, userContext, attachmentStorageMock);
    }

    private (
        HsrEndGameApplicationService Service,
        Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>> EndGameApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock,
        UserDbContext UserContext
        ) SetupIntegrationTest()
    {
        var cardService = new HsrEndGameCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrEndGameCardService>>());

        var endGameApiMock = new Mock<IApiService<HsrEndInformation, HsrEndGameApiContext>>();

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            S3TestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var loggerMock = new Mock<ILogger<HsrEndGameApplicationService>>();

        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        cardService.InitializeAsync().Wait();

        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new HsrEndGameApplicationService(
            cardService,
            imageUpdaterService,
            endGameApiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, endGameApiMock, imageUpdaterMock, gameRoleApiMock, attachmentStorageMock, userContext);
    }

    private (
        HsrEndGameApplicationService Service,
        IAttachmentStorageService AttachmentStorageService,
        Dictionary<string, MemoryStream> StoredAttachments,
        UserDbContext UserContext
        ) SetupRealApiIntegrationTest()
    {
        var cardService = new HsrEndGameCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrEndGameCardService>>());

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var endGameApiService = new HsrEndGameApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<HsrEndGameApiService>>());

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

        var service = new HsrEndGameApplicationService(
            cardService,
            imageUpdaterService,
            endGameApiService,
            gameRoleApiService,
            userContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<HsrEndGameApplicationService>>());

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
            GameUid = "800000000",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static async Task<HsrEndInformation> LoadTestDataAsync(string filename)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var filePath = Path.Combine(TestDataPath, filename);
        var json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<HsrEndInformation>(json, options);
        if (result == null) throw new InvalidOperationException($"Failed to deserialize {filename}");

        return result;
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
