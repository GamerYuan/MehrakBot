#region

using System.Text.Json;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Hsr;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Hsr.Memory;

[Parallelizable(ParallelScope.Self)]
public class HsrMemoryApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
        });
    }

    [Test]
    public async Task ExecuteAsync_MemoryApiError_ReturnsApiError()
    {
        // Arrange
        var (service, memoryApiMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrMemoryInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("Memory of Chaos data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_NoData_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, memoryApiMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
                  .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var memoryData = new HsrMemoryInformation
        {
            HasData = false,
            Groups = [],
            AllFloorDetail = null,
            MaxFloor = "0",
            StarNum = 0,
            BattleNum = 0,
            MaxFloorId = 0,
            StartTime = new ScheduleTime { Year = 2024, Month = 1, Day = 1, Hour = 0, Minute = 0 },
            EndTime = new ScheduleTime { Year = 2024, Month = 1, Day = 15, Hour = 0, Minute = 0 },
            ScheduleId = 1
        };

        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrMemoryInformation>.Success(memoryData));

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
        });
    }

    [Test]
    public async Task ExecuteAsync_BattleNumZero_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, memoryApiMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var memoryData = new HsrMemoryInformation
        {
            HasData = true,
            Groups = [],
            AllFloorDetail = [],
            MaxFloor = "0",
            StarNum = 0,
            BattleNum = 0, // Zero battles = no clear records
            MaxFloorId = 0,
            StartTime = new ScheduleTime { Year = 2024, Month = 1, Day = 1, Hour = 0, Minute = 0 },
            EndTime = new ScheduleTime { Year = 2024, Month = 1, Day = 15, Hour = 0, Minute = 0 },
            ScheduleId = 1
        };

        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
        .ReturnsAsync(Result<HsrMemoryInformation>.Success(memoryData));

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.IsEphemeral, Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
                Does.Contain("no clear records").IgnoreCase);
        });
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsBotError()
    {
        // Arrange
        var (service, memoryApiMock, imageUpdaterMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var memoryData = await LoadTestDataAsync("Moc_TestData_1.json");
        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrMemoryInformation>.Success(memoryData));

        // Make image update fail
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
            Assert.That(result.ErrorMessage, Does.Contain("image"));
        });
    }

    [Test]
    [TestCase("Moc_TestData_1.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(string testDataFile)
    {
        // Arrange
        var (service, memoryApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var memoryData = await LoadTestDataAsync(testDataFile);
        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrMemoryInformation>.Success(memoryData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
                  .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrMemoryInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
            Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
            Assert.That(result.Data.Components.OfType<CommandText>()
                .Any(x => x.Content.Contains("Memory of Chaos Summary")),
                Is.True);
        });
    }

    [Test]
    [TestCase("Moc_TestData_1.json")]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly(string testDataFile)
    {
        // Arrange
        var (service, memoryApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var memoryData = await LoadTestDataAsync(testDataFile);
        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<HsrMemoryInformation>.Success(memoryData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<HsrMemoryInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new HsrMemoryApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar images were updated
        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Moc_TestData_1.json")]
    [TestCase("Moc_TestData_2.json")]
    [TestCase("Moc_TestData_3.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, memoryApiMock, _, gameRoleApiMock) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var memoryData = await LoadTestDataAsync(testDataFile);
        memoryApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
             .ReturnsAsync(Result<HsrMemoryInformation>.Success(memoryData));

        var context = new HsrMemoryApplicationContext(MongoTestHelper.Instance.GetUniqueUserId())
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

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
        Assert.That(attachment!.Content.Length, Is.GreaterThan(0), "Expected a non-empty card image");

        // Save the generated card for manual inspection
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(
            outputDirectory,
            $"HsrMemoryIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

        attachment.Content.Position = 0;
        await using var fileStream = File.Create(outputImagePath);
        await attachment.Content.CopyToAsync(fileStream);
    }

    [Test]
    [Explicit("This test calls real API - only run manually")]
    public async Task IntegrationTest_WithRealApi_FullFlow()
    {
        // This test requires real credentials and should only be run manually
        // It demonstrates the full integration with the actual HoYoLab API

        var config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build()
            .GetRequiredSection("Credentials");

        ulong testLtUid = ulong.Parse(config["LtUid"] ?? "0");
        string? testLToken = config["LToken"];

        Assert.Multiple(() =>
        {
            Assert.That(testLtUid, Is.GreaterThan(0), "LtUid must be set in appsettings.test.json");
            Assert.That(testLToken, Is.Not.Null.And.Not.Empty, "LToken must be set in appsettings.test.json");
        });

        var service = SetupRealApiIntegrationTest();

        var context = new HsrMemoryApplicationContext(MongoTestHelper.Instance.GetUniqueUserId())
        {
            LtUid = testLtUid,
            LToken = testLToken!,
            Server = Server.Asia
        };

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True, $"API call failed: {result.ErrorMessage}");

        if (result.IsSuccess)
        {
            var attachment = result.Data!.Components.OfType<CommandAttachment>().FirstOrDefault();
            Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
            Assert.That(attachment!.Content.Length, Is.GreaterThan(0));

            // Save output
            string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "RealApi");
            Directory.CreateDirectory(outputDirectory);
            string outputImagePath = Path.Combine(outputDirectory, "HsrMemoryRealApi.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        HsrMemoryApplicationService Service,
        Mock<IApiService<HsrMemoryInformation, BaseHoYoApiContext>> MemoryApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<HsrMemoryInformation>> CardServiceMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<HsrMemoryInformation>>();
        var memoryApiMock = new Mock<IApiService<HsrMemoryInformation, BaseHoYoApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<HsrMemoryApplicationService>>();

        var service = new HsrMemoryApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            memoryApiMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, memoryApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock);
    }

    private static (
        HsrMemoryApplicationService Service,
        Mock<IApiService<HsrMemoryInformation, BaseHoYoApiContext>> MemoryApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new HsrMemoryCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrMemoryCardService>>());

        var memoryApiMock = new Mock<IApiService<HsrMemoryInformation, BaseHoYoApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<HsrMemoryApplicationService>>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new HsrMemoryApplicationService(
            cardService,
            imageUpdaterService,
            memoryApiMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, memoryApiMock, imageUpdaterMock, gameRoleApiMock);
    }

    private static HsrMemoryApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new HsrMemoryCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<HsrMemoryCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Real Memory API service (internal, use reflection or create instance)
        var memoryApiService = new HsrMemoryApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<HsrMemoryApiService>>());

        // Real game role API service
        var gameRoleApiService = new GameRoleApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<GameRoleApiService>>());

        // Real image updater service
        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactory.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new HsrMemoryApplicationService(
            cardService,
            imageUpdaterService,
            memoryApiService,
            gameRoleApiService,
            Mock.Of<ILogger<HsrMemoryApplicationService>>());

        return service;
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

    private static async Task<HsrMemoryInformation> LoadTestDataAsync(string filename)
    {
        string filePath = Path.Combine(TestDataPath, filename);
        string json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<HsrMemoryInformation>(json);

        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
