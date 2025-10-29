﻿#region

using System.Text.Json;
using Mehrak.Application.Services.Zzz.Assault;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Zzz;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Zzz.Assault;

[Parallelizable(ParallelScope.Self)]
public class ZzzAssaultApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, _, gameRoleApiMock, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new ZzzAssaultApplicationContext(1)
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
    public async Task ExecuteAsync_AssaultApiError_ReturnsApiError()
    {
        // Arrange
        var (service, assaultApiMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzAssaultData>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new ZzzAssaultApplicationContext(1)
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
            Assert.That(result.ErrorMessage, Does.Contain("Deadly Assault data"));
        });
    }

    [Test]
    public async Task ExecuteAsync_NoData_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, assaultApiMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
        .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var assaultData = new ZzzAssaultData
        {
            HasData = false,
            List = [],
            StartTime = new ScheduleTime { Year = 2024, Month = 1, Day = 1, Hour = 0, Minute = 0 },
            EndTime = new ScheduleTime { Year = 2024, Month = 1, Day = 15, Hour = 0, Minute = 0 },
            RankPercent = 0,
            TotalScore = 0,
            TotalStar = 0
        };

        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
              .ReturnsAsync(Result<ZzzAssaultData>.Success(assaultData));

        var context = new ZzzAssaultApplicationContext(1)
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
    public async Task ExecuteAsync_EmptyList_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, assaultApiMock, _, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var assaultData = new ZzzAssaultData
        {
            HasData = true,
            List = [], // Empty list = no clear records
            StartTime = new ScheduleTime { Year = 2024, Month = 1, Day = 1, Hour = 0, Minute = 0 },
            EndTime = new ScheduleTime { Year = 2024, Month = 1, Day = 15, Hour = 0, Minute = 0 },
            RankPercent = 0,
            TotalScore = 0,
            TotalStar = 0
        };

        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzAssaultData>.Success(assaultData));

        var context = new ZzzAssaultApplicationContext(1)
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
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsApiError()
    {
        // Arrange
        var (service, assaultApiMock, imageUpdaterMock, gameRoleApiMock, _) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var assaultData = await LoadTestDataAsync("Da_TestData_1.json");
        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzAssaultData>.Success(assaultData));

        // Make image update fail
        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new ZzzAssaultApplicationContext(1)
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
    [TestCase("Da_TestData_1.json")]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard(string testDataFile)
    {
        // Arrange
        var (service, assaultApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var assaultData = await LoadTestDataAsync(testDataFile);
        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzAssaultData>.Success(assaultData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        imageUpdaterMock.Setup(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzAssaultData>>()))
            .ReturnsAsync(cardStream);

        var context = new ZzzAssaultApplicationContext(1)
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
                .Any(x => x.Content.Contains("Deadly Assault Summary")),
                Is.True);
        });
    }

    [Test]
    [TestCase("Da_TestData_1.json")]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly(string testDataFile)
    {
        // Arrange
        var (service, assaultApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock) = SetupMocks();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var assaultData = await LoadTestDataAsync(testDataFile);
        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzAssaultData>.Success(assaultData));

        imageUpdaterMock.Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);
        imageUpdaterMock.Setup(x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<ZzzAssaultData>>()))
            .ReturnsAsync(cardStream);

        var context = new ZzzAssaultApplicationContext(1)
        {
            LtUid = 1ul,
            LToken = "test",
            Server = Server.Asia
        };

        // Act
        await service.ExecuteAsync(context);

        // Assert: Verify avatar, buddy, boss, and buff images were updated
        imageUpdaterMock.Verify(
            x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
        imageUpdaterMock.Verify(
            x => x.UpdateMultiImageAsync(It.IsAny<IMultiImageData>(), It.IsAny<IMultiImageProcessor>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Integration Tests

    [Test]
    [TestCase("Da_TestData_1.json")]
    [TestCase("Da_TestData_2.json")]
    public async Task IntegrationTest_WithRealCardService_GeneratesCard(string testDataFile)
    {
        // Arrange
        var (service, assaultApiMock, _, gameRoleApiMock) = SetupIntegrationTest();

        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var assaultData = await LoadTestDataAsync(testDataFile);
        assaultApiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<ZzzAssaultData>.Success(assaultData));

        var context = new ZzzAssaultApplicationContext(MongoTestHelper.Instance.GetUniqueUserId())
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
            $"ZzzAssaultIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}.jpg");

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

        var context = new ZzzAssaultApplicationContext(MongoTestHelper.Instance.GetUniqueUserId())
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
            string outputImagePath = Path.Combine(outputDirectory, "ZzzAssaultRealApi.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        ZzzAssaultApplicationService Service,
        Mock<IApiService<ZzzAssaultData, BaseHoYoApiContext>> AssaultApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<ZzzAssaultData>> CardServiceMock
        ) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<ZzzAssaultData>>();
        var assaultApiMock = new Mock<IApiService<ZzzAssaultData, BaseHoYoApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<ZzzAssaultApplicationService>>();

        var service = new ZzzAssaultApplicationService(
            cardServiceMock.Object,
            imageUpdaterMock.Object,
            assaultApiMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        return (service, assaultApiMock, imageUpdaterMock, gameRoleApiMock, cardServiceMock);
    }

    private static (
        ZzzAssaultApplicationService Service,
        Mock<IApiService<ZzzAssaultData, BaseHoYoApiContext>> AssaultApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new ZzzAssaultCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzAssaultCardService>>());

        var assaultApiMock = new Mock<IApiService<ZzzAssaultData, BaseHoYoApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<ZzzAssaultApplicationService>>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new ZzzAssaultApplicationService(
            cardService,
            imageUpdaterService,
            assaultApiMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object);

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, assaultApiMock, imageUpdaterMock, gameRoleApiMock);
    }

    private static ZzzAssaultApplicationService SetupRealApiIntegrationTest()
    {
        // Use all real services - no mocks
        var cardService = new ZzzAssaultCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzAssaultCardService>>());

        // Real HTTP client factory
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        // Real Assault API service
        var assaultApiService = new ZzzAssaultApiService(
            httpClientFactory.Object,
            Mock.Of<ILogger<ZzzAssaultApiService>>());

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

        var service = new ZzzAssaultApplicationService(
            cardService,
            imageUpdaterService,
            assaultApiService,
            gameRoleApiService,
            Mock.Of<ILogger<ZzzAssaultApplicationService>>());

        return service;
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

    private static async Task<ZzzAssaultData> LoadTestDataAsync(string filename)
    {
        string filePath = Path.Combine(TestDataPath, filename);
        string json = await File.ReadAllTextAsync(filePath);
        var result = JsonSerializer.Deserialize<ZzzAssaultData>(json);

        return result ?? throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}
