#region

using System.Text.Json;
using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.Abyss;

[Parallelizable(ParallelScope.Self)]
public class GenshinAbyssApplicationServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin");

    #region Unit Tests

    [Test]
    public async Task ExecuteAsync_InvalidLogin_ReturnsAuthError()
    {
        // Arrange
        var (service, _, gameRoleApiMock, _, _, _) = SetupMocks();
        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "Invalid credentials"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
        Assert.That(result.ErrorMessage, Does.Contain("invalid hoyolab uid or cookies").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_AbyssApiError_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Failure(StatusCode.ExternalServerError, "API Error"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
        Assert.That(result.ErrorMessage, Does.Contain("Spiral Abyss data"));
    }

    [Test]
    public async Task ExecuteAsync_FloorNotFound_ReturnsNoClearRecords()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, _, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = new GenshinAbyssInformation
        {
            Floors = new List<Floor>
            {
                new() { Index = 11, Levels = new List<Level>() }
            },
            RevealRank = new List<AbyssRankAvatar>(),
            DefeatRank = new List<AbyssRankAvatar>(),
            DamageRank = new List<AbyssRankAvatar>(),
            TakeDamageRank = new List<AbyssRankAvatar>(),
            NormalSkillRank = new List<AbyssRankAvatar>(),
            EnergySkillRank = new List<AbyssRankAvatar>()
        };

        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Data!.IsEphemeral, Is.True);
        Assert.That(result.Data.Components.OfType<CommandText>().First().Content,
            Does.Contain("no clear records").IgnoreCase);
    }

    [Test]
    public async Task ExecuteAsync_CharacterListApiError_ReturnsApiError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, _, characterApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Failure(StatusCode.ExternalServerError,
                "Character API Error"));

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.ApiError));
        Assert.That(result.ErrorMessage, Does.Contain("Character List"));
    }

    [Test]
    public async Task ExecuteAsync_ImageUpdateFails_ReturnsBotError()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, _) = SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        // Make image update fail
        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(false);

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
        Assert.That(result.ErrorMessage, Does.Contain("image"));
    }

    [Test]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccessWithCard()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock) =
            SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<GenshinEndGameGenerationContext<GenshinAbyssInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));
        Assert.That(result.Data.Components.OfType<CommandAttachment>().Any(), Is.True);
        Assert.That(result.Data.Components.OfType<CommandText>().Any(x => x.Content.Contains("Spiral Abyss Summary")),
            Is.True);
    }

    [Test]
    public async Task ExecuteAsync_VerifyImageUpdatesCalledCorrectly()
    {
        // Arrange
        var (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock) =
            SetupMocks();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>("Abyss_TestData_1.json");
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        imageUpdaterMock
            .Setup(x => x.UpdateImageAsync(It.IsAny<IImageData>(), It.IsAny<IImageProcessor>()))
            .ReturnsAsync(true);

        var cardStream = new MemoryStream();
        cardServiceMock
            .Setup(x => x.GetCardAsync(It.IsAny<GenshinEndGameGenerationContext<GenshinAbyssInformation>>()))
            .ReturnsAsync(cardStream);

        var context = new GenshinAbyssApplicationContext(1, ("floor", (object)12u), ("ltuid", 1ul), ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        await service.ExecuteAsync(context);

        // Assert Verify avatar images were updated (for battles + rank avatars)
        imageUpdaterMock.Verify(x => x.UpdateImageAsync(
                It.Is<IImageData>(d => d != null),
                It.IsAny<IImageProcessor>()),
            Times.AtLeastOnce);
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
        var (service, abyssApiMock, gameRoleApiMock, _, characterApiMock, _) = SetupIntegrationTest();

        gameRoleApiMock
            .Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));

        var abyssData = await LoadTestDataAsync<GenshinAbyssInformation>(testDataFile);
        abyssApiMock
            .Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GenshinAbyssInformation>.Success(abyssData));

        var charList = CreateTestCharacterList();
        characterApiMock
            .Setup(x => x.GetAllCharactersAsync(It.IsAny<CharacterApiContext>()))
            .ReturnsAsync(Result<IEnumerable<GenshinBasicCharacterData>>.Success(charList));

        var context = new GenshinAbyssApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(),
            ("floor", (object)floor),
            ("ltuid", 1ul),
            ("ltoken", "test"),
            ("server", Server.Asia));

        // Act
        var result = await service.ExecuteAsync(context);

        // Assert
        Assert.That(result.IsSuccess, Is.True, $"Expected success but got: {result.ErrorMessage}");
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data!.Components.Count(), Is.GreaterThan(0));

        var attachment = result.Data.Components.OfType<CommandAttachment>().FirstOrDefault();
        Assert.That(attachment, Is.Not.Null, "Expected an attachment component");
        Assert.That(attachment!.Content.Length, Is.GreaterThan(0), "Expected a non-empty card image");

        // Save the generated card for manual inspection
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output", "Integration");
        Directory.CreateDirectory(outputDirectory);
        string outputImagePath = Path.Combine(outputDirectory,
            $"AbyssIntegration_{Path.GetFileNameWithoutExtension(testDataFile)}_Floor{floor}.jpg");

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

        // NOTE: Replace these with actual test credentials
        const ulong testLtUid = 0ul; // Replace with real ltuid
        const string testLToken = ""; // Replace with real ltoken
        const uint floor = 12u;

        if (testLtUid == 0 || string.IsNullOrEmpty(testLToken))
        {
            Assert.Ignore("Real API credentials not configured");
            return;
        }

        var (service, _, _, _, _, _) = SetupIntegrationTest();

        var context = new GenshinAbyssApplicationContext(
            MongoTestHelper.Instance.GetUniqueUserId(),
            ("floor", (object)floor),
            ("ltuid", testLtUid),
            ("ltoken", testLToken),
            ("server", Server.Asia));

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
            string outputImagePath = Path.Combine(outputDirectory, $"AbyssRealApi_Floor{floor}.jpg");

            attachment.Content.Position = 0;
            await using var fileStream = File.Create(outputImagePath);
            await attachment.Content.CopyToAsync(fileStream);
        }
    }

    #endregion

    #region Helper Methods

    private static (
        GenshinAbyssApplicationService Service,
        Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>> AbyssApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>>
        CharacterApiMock,
        Mock<ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation>>
        CardServiceMock
        ) SetupMocks()
    {
        var cardServiceMock =
            new Mock<ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation>>();
        var abyssApiMock = new Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>>();
        var characterApiMock =
            new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>>();
        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinAbyssApplicationService>>();

        var service = new GenshinAbyssApplicationService(
            cardServiceMock.Object,
            abyssApiMock.Object,
            characterApiMock.Object,
            imageUpdaterMock.Object,
            gameRoleApiMock.Object,
            loggerMock.Object
        );

        return (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardServiceMock);
    }

    private static (
        GenshinAbyssApplicationService Service,
        Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>> AbyssApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<IImageUpdaterService> ImageUpdaterMock,
        Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>>
        CharacterApiMock,
        ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation> CardService
        ) SetupIntegrationTest()
    {
        // Use real card service with MongoTestHelper for image repository
        var cardService = new GenshinAbyssCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinAbyssCardService>>());

        var abyssApiMock = new Mock<IApiService<GenshinAbyssInformation, BaseHoYoApiContext>>();
        var characterApiMock =
            new Mock<ICharacterApiService<GenshinBasicCharacterData, GenshinCharacterDetail, CharacterApiContext>>();

        // Use real image updater service
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var imageUpdaterService = new ImageUpdaterService(
            MongoTestHelper.Instance.ImageRepository,
            httpClientFactoryMock.Object,
            Mock.Of<ILogger<ImageUpdaterService>>());

        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var loggerMock = new Mock<ILogger<GenshinAbyssApplicationService>>();

        // Initialize card service
        cardService.InitializeAsync().Wait();

        var service = new GenshinAbyssApplicationService(
            cardService,
            abyssApiMock.Object,
            characterApiMock.Object,
            imageUpdaterService,
            gameRoleApiMock.Object,
            loggerMock.Object
        );

        var imageUpdaterMock = new Mock<IImageUpdaterService>();
        return (service, abyssApiMock, gameRoleApiMock, imageUpdaterMock, characterApiMock, cardService);
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
        return new List<GenshinBasicCharacterData>
        {
            new()
            {
                Id = 10000032, Icon = "", Name = "Bennett", Element = "Pyro", Level = 80, Rarity = 4,
                ActivedConstellationNum = 6, Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new()
            {
                Id = 10000037, Icon = "", Name = "Ganyu", Element = "Cryo", Level = 90, Rarity = 5,
                ActivedConstellationNum = 1, Weapon = new Weapon { Icon = "", Name = "Bow" }
            },
            new()
            {
                Id = 10000063, Icon = "", Name = "Shenhe", Element = "Cryo", Level = 90, Rarity = 5,
                ActivedConstellationNum = 2, Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new()
            {
                Id = 10000089, Icon = "", Name = "Mika", Element = "Cryo", Level = 90, Rarity = 5,
                ActivedConstellationNum = 6, Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new()
            {
                Id = 10000103, Icon = "", Name = "Furina", Element = "Hydro", Level = 90, Rarity = 5,
                ActivedConstellationNum = 3, Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new()
            {
                Id = 10000106, Icon = "", Name = "Clorinde", Element = "Electro", Level = 90, Rarity = 5,
                ActivedConstellationNum = 0, Weapon = new Weapon { Icon = "", Name = "Sword" }
            },
            new()
            {
                Id = 10000107, Icon = "", Name = "Emilie", Element = "Dendro", Level = 90, Rarity = 5,
                ActivedConstellationNum = 4, Weapon = new Weapon { Icon = "", Name = "Polearm" }
            },
            new()
            {
                Id = 10000112, Icon = "", Name = "Chasca", Element = "Anemo", Level = 90, Rarity = 5,
                ActivedConstellationNum = 5, Weapon = new Weapon { Icon = "", Name = "Bow" }
            }
        };
    }

    private static async Task<T> LoadTestDataAsync<T>(string filename)
    {
        string filePath = Path.Combine(TestDataPath, filename);
        string json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<T>(json) ??
               throw new InvalidOperationException($"Failed to deserialize {filename}");
    }

    #endregion
}