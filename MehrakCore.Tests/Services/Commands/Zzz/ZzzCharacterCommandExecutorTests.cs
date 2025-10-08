using Mehrak.Application.Services.Zzz.Character;
using Mehrak.Bot.Executors.Zzz;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi;
using Mehrak.GameApi.Zzz.Types;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Services;
using MehrakCore.Services.Commands;
using MehrakCore.Services.Commands.Zzz.Character;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using MehrakCore.Utility;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NetCord;
using NetCord.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MehrakCore.Tests.Services.Commands.Zzz;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzCharacterCommandExecutorTests
{
    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestGuid = "test-guid-12345";
    private const string TestGameUid = "1300000000";

    private ZzzCharacterCommandExecutor m_Executor = null!;
    private Mock<ICharacterApi<ZzzBasicAvatarData, ZzzFullAvatarData>> m_CharacterApiMock = null!;
    private GameRecordApiService m_GameRecordApiService = null!;
    private Mock<ICharacterCardService<ZzzFullAvatarData>> m_CharacterCardServiceMock = null!;
    private Mock<ImageUpdaterService<ZzzFullAvatarData>> m_ImageUpdaterServiceMock = null!;
    private UserRepository m_UserRepository = null!;
    private Mock<ILogger<ZzzCharacterCommandExecutor>> m_LoggerMock = null!;
    private DiscordTestHelper m_DiscordTestHelper = null!;
    private Mock<IInteractionContext> m_ContextMock = null!;
    private SlashCommandInteraction m_Interaction = null!;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock = null!;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock = null!;
    private Mock<IDistributedCache> m_DistributedCacheMock = null!;
    private Mock<IAuthenticationMiddlewareService> m_AuthenticationMiddlewareMock = null!;
    private TokenCacheService m_TokenCacheService = null!;
    private Mock<ICharacterCacheService> m_CharacterCacheServiceMock = null!;

    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    [SetUp]
    public void Setup()
    {
        // Initialize test helpers
        m_DiscordTestHelper = new DiscordTestHelper();

        // Create mocks for dependencies
        m_CharacterApiMock = new Mock<ICharacterApi<ZzzBasicAvatarData, ZzzFullAvatarData>>();
        m_CharacterCardServiceMock = new Mock<ICharacterCardService<ZzzFullAvatarData>>();
        m_LoggerMock = new Mock<ILogger<ZzzCharacterCommandExecutor>>();
        m_DistributedCacheMock = new Mock<IDistributedCache>();
        m_AuthenticationMiddlewareMock = new Mock<IAuthenticationMiddlewareService>();

        // Set up authentication middleware to return TestGuid
        m_AuthenticationMiddlewareMock.Setup(x => x.RegisterAuthenticationListener(
                It.IsAny<ulong>(), It.IsAny<IAuthenticationListener>()))
            .Returns(TestGuid);

        ImageRepository imageRepository =
            new(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_ImageUpdaterServiceMock = new Mock<ImageUpdaterService<ZzzFullAvatarData>>(
            imageRepository,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<ImageUpdaterService<ZzzFullAvatarData>>>());

        // Set up mocked HTTP handler and client factory
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        HttpClient httpClient = new(m_HttpMessageHandlerMock.Object);
        m_HttpClientFactoryMock.Setup(f => f.CreateClient("Default")).Returns(httpClient);

        // Create real services with mocked dependencies
        m_GameRecordApiService = new GameRecordApiService(
            m_HttpClientFactoryMock.Object,
            NullLogger<GameRecordApiService>.Instance);

        m_TokenCacheService = new TokenCacheService(
            m_DistributedCacheMock.Object,
            NullLogger<TokenCacheService>.Instance);

        // Use real UserRepository with in-memory MongoDB
        m_UserRepository =
            new UserRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<UserRepository>.Instance);

        m_CharacterCacheServiceMock = new Mock<ICharacterCacheService>();

        // Set up default behavior for GetAliases to return empty dictionary
        m_CharacterCacheServiceMock
            .Setup(x => x.GetAliases(It.IsAny<GameName>()))
            .Returns([]);

        // Set up default distributed cache behavior
        SetupDistributedCacheMock();

        m_TestUserId = MongoTestHelper.Instance.GetUniqueUserId();

        // Set up interaction context
        m_Interaction = m_DiscordTestHelper.CreateCommandInteraction(m_TestUserId);
        m_ContextMock = new Mock<IInteractionContext>();
        m_ContextMock.Setup(x => x.Interaction).Returns(m_Interaction);

        // Set up Discord test helper to capture responses
        m_DiscordTestHelper.SetupRequestCapture();

        // Create the service under test
        m_Executor = new ZzzCharacterCommandExecutor(
            m_CharacterApiMock.Object,
            m_CharacterCacheServiceMock.Object,
            m_CharacterCardServiceMock.Object,
            m_ImageUpdaterServiceMock.Object,
            m_UserRepository,
            m_TokenCacheService,
            m_AuthenticationMiddlewareMock.Object,
            m_GameRecordApiService,
            m_LoggerMock.Object
        )
        {
            Context = m_ContextMock.Object
        };
    }

    [TearDown]
    public async Task TearDown()
    {
        m_DiscordTestHelper.Dispose();
        await m_UserRepository.DeleteUserAsync(m_TestUserId);
    }

    #region ExecuteAsync Tests

    [Test]
    public void ExecuteAsync_WhenParametersCountInvalid_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync("only1"));
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync("a", Regions.Asia));
        Assert.ThrowsAsync<ArgumentException>(async () => await m_Executor.ExecuteAsync("a", Regions.Asia, 1u, "extra"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserHasNoProfile_ReturnsNoProfileMessage()
    {
        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("You do not have a profile with this ID"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerProvidedAndNoLastUsedRegion_ReturnsNoServerMessage()
    {
        await CreateOrUpdateTestUserAsync(1);
        await m_Executor.ExecuteAsync("Jane", null, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("No cached server found. Please select a server"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoServerProvidedButHasLastUsedRegionForHsr_UsesThatAndShowsAuthModal()
    {
        // Note: Executor erroneously checks HonkaiStarRail cached server, so
        // set that
        await CreateOrUpdateTestUserAsync(1, lastUsedRegionForHsr: Regions.Asia);
        await m_Executor.ExecuteAsync("Jane", null, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring($"auth_modal:{TestGuid}:1"));
    }

    [Test]
    public async Task ExecuteAsync_WhenUserNotAuthenticated_ShowsAuthModal()
    {
        await CreateOrUpdateTestUserAsync(1);
        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring($"auth_modal:{TestGuid}:1"));
        m_AuthenticationMiddlewareMock.Verify(x => x.RegisterAuthenticationListener(m_TestUserId, m_Executor), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenAuthenticatedAndCharacterFound_SendsCharacterCard()
    {
        await CreateOrUpdateTestUserAsync(1, gameProfileForZzz: (Regions.Asia, TestGameUid));
        // Provide token in cache
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Mock character list and details
        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(
            [
                new()
                {
                    Id = 1261,
                    Level = 60,
                    Name = "Jane",
                    FullName = "Jane Doe",
                    CampName = "CISRT",
                    AvatarProfession = 3,
                    Rarity = "S",
                    GroupIconPath = string.Empty,
                    HollowIconPath = string.Empty,
                    Rank = 0,
                    IsChosen = true,
                    RoleSquareUrl = string.Empty,
                    AwakenState = "AwakenStateNotVisible"
                }
            ]);

        ZzzFullAvatarData characterDetail = await LoadTestCharacterDetailAsync("Jane_TestData.json");
        m_CharacterApiMock
            .Setup(x => x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>(), 1261u))
            .ReturnsAsync(ApiResult<ZzzFullAvatarData>.Success(characterDetail));

        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<ZzzFullAvatarData>(), TestGameUid))
            .ReturnsAsync(new MemoryStream(new byte[128]));

        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);

        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("character_card.jpg").Or.Contain("Command execution completed"));
        m_CharacterCardServiceMock.Verify(x => x.GenerateCharacterCardAsync(It.IsAny<ZzzFullAvatarData>(), TestGameUid), Times.Once);
        m_ImageUpdaterServiceMock.Verify(x => x.UpdateDataAsync(It.IsAny<ZzzFullAvatarData>(), It.IsAny<IEnumerable<Dictionary<string, string>>>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenAuthenticatedButCharacterNotFound_SendsError()
    {
        await CreateOrUpdateTestUserAsync(1, gameProfileForZzz: (Regions.Asia, TestGameUid));
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync([]);

        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character not found"));
    }

    [Test]
    public async Task ExecuteAsync_WhenAuthenticated_UsesAliasResolution()
    {
        await CreateOrUpdateTestUserAsync(1, gameProfileForZzz: (Regions.Asia, TestGameUid));
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Aliases: jd -> Jane
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "jd", "Jane" }
        };
        m_CharacterCacheServiceMock.Setup(x => x.GetAliases(GameName.ZenlessZoneZero)).Returns(aliases);

        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(
            [
                new()
                {
                    Id = 1261,
                    Level = 60,
                    Name = "Jane",
                    FullName = "Jane Doe",
                    CampName = "CISRT",
                    AvatarProfession = 3,
                    Rarity = "S",
                    GroupIconPath = string.Empty,
                    HollowIconPath = string.Empty,
                    Rank = 0,
                    IsChosen = true,
                    RoleSquareUrl = string.Empty,
                    AwakenState = "AwakenStateNotVisible"
                }
            ]);

        ZzzFullAvatarData characterDetail = await LoadTestCharacterDetailAsync("Jane_TestData.json");
        m_CharacterApiMock
            .Setup(x => x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>(), 1261u))
            .ReturnsAsync(ApiResult<ZzzFullAvatarData>.Success(characterDetail));

        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<ZzzFullAvatarData>(), TestGameUid))
            .ReturnsAsync(new MemoryStream(new byte[64]));

        await m_Executor.ExecuteAsync("jd", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("character_card.jpg").Or.Contain("Command execution completed"));
        m_CharacterCacheServiceMock.Verify(x => x.GetAliases(GameName.ZenlessZoneZero), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenCharacterDetailApiFails_SendsErrorMessage()
    {
        await CreateOrUpdateTestUserAsync(1, gameProfileForZzz: (Regions.Asia, TestGameUid));
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(
            [
                new()
                {
                    Id = 1261,
                    Level = 60,
                    Name = "Jane",
                    FullName = "Jane Doe",
                    CampName = "CISRT",
                    AvatarProfession = 3,
                    Rarity = "S",
                    GroupIconPath = string.Empty,
                    HollowIconPath = string.Empty,
                    Rank = 0,
                    IsChosen = true,
                    RoleSquareUrl = string.Empty,
                    AwakenState = "AwakenStateNotVisible"
                }
            ]);

        m_CharacterApiMock
            .Setup(x => x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>(), 1261u))
            .ReturnsAsync(ApiResult<ZzzFullAvatarData>.Failure(HttpStatusCode.BadGateway, "Failed to fetch"));

        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Failed to fetch"));
    }

    [Test]
    public async Task ExecuteAsync_WhenCharacterDetailApiReturnsNullData_SendsErrorMessage()
    {
        await CreateOrUpdateTestUserAsync(1, gameProfileForZzz: (Regions.Asia, TestGameUid));
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(
            [
                new()
                {
                    Id = 1261,
                    Level = 60,
                    Name = "Jane",
                    FullName = "Jane Doe",
                    CampName = "CISRT",
                    AvatarProfession = 3,
                    Rarity = "S",
                    GroupIconPath = string.Empty,
                    HollowIconPath = string.Empty,
                    Rank = 0,
                    IsChosen = true,
                    RoleSquareUrl = string.Empty,
                    AwakenState = "AwakenStateNotVisible"
                }
            ]);

        // Success but null data
        m_CharacterApiMock
            .Setup(x => x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>(), 1261u))
            .ReturnsAsync(ApiResult<ZzzFullAvatarData>.Success(null!));

        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Contains.Substring("Character data not found"));
    }

    [Test]
    public async Task ExecuteAsync_WhenNoSavedGameUid_CallsGameRecordApi_AndSendsCard()
    {
        await CreateOrUpdateTestUserAsync(1); // No saved game uid
        m_DistributedCacheMock.Setup(x => x.GetAsync($"TokenCache_{m_TestUserId}_{TestLtUid}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(TestLToken));

        // Mock Game Role API
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(Regions.Asia));

        // Mock character API
        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(
            [
                new()
                {
                    Id = 1261,
                    Level = 60,
                    Name = "Jane",
                    FullName = "Jane Doe",
                    CampName = "CISRT",
                    AvatarProfession = 3,
                    Rarity = "S",
                    GroupIconPath = string.Empty,
                    HollowIconPath = string.Empty,
                    Rank = 0,
                    IsChosen = true,
                    RoleSquareUrl = string.Empty,
                    AwakenState = "AwakenStateNotVisible"
                }
            ]);

        ZzzFullAvatarData characterDetail = await LoadTestCharacterDetailAsync("Jane_TestData.json");
        m_CharacterApiMock
            .Setup(x => x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>(), 1261u))
            .ReturnsAsync(ApiResult<ZzzFullAvatarData>.Success(characterDetail));

        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<ZzzFullAvatarData>(), TestGameUid))
            .ReturnsAsync(new MemoryStream(new byte[128]));

        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        // Verify profile updated
        UserModel? updatedUser = await m_UserRepository.GetUserAsync(m_TestUserId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updatedUser?.Profiles?.First().GameUids?[GameName.ZenlessZoneZero][Regions.Asia.ToString()], Is.EqualTo(TestGameUid));
            Assert.That(updatedUser?.Profiles?.First().LastUsedRegions?[GameName.ZenlessZoneZero], Is.EqualTo(Regions.Asia));
            Assert.That(response, Does.Contain("character_card.jpg").Or.Contain("Command execution completed"));
        }
    }

    #endregion ExecuteAsync Tests

    #region OnAuthenticationCompletedAsync Tests

    [Test]
    public async Task OnAuthenticationCompletedAsync_AuthenticationFailed_LogsWarning()
    {
        AuthenticationResult result = AuthenticationResult.Failure(m_TestUserId, "Authentication failed");
        await m_Executor.OnAuthenticationCompletedAsync(result);

        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication failed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task OnAuthenticationCompletedAsync_WhenSuccess_UsesPendingStateAndSendsCard()
    {
        // First call ExecuteAsync to set pending state
        await CreateOrUpdateTestUserAsync(1); // No token so it will set pending and show modal
        await m_Executor.ExecuteAsync("Jane", Regions.Asia, 1u);
        m_DiscordTestHelper.ClearCapturedRequests();

        // Prepare API mocks for the auth completion
        SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode.OK, CreateValidGameRecordResponse(Regions.Asia));
        m_CharacterApiMock
            .Setup(x => x.GetAllCharactersAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>()))
            .ReturnsAsync(
            [
                new()
                {
                    Id = 1261,
                    Level = 60,
                    Name = "Jane",
                    FullName = "Jane Doe",
                    CampName = "CISRT",
                    AvatarProfession = 3,
                    Rarity = "S",
                    GroupIconPath = string.Empty,
                    HollowIconPath = string.Empty,
                    Rank = 0,
                    IsChosen = true,
                    RoleSquareUrl = string.Empty,
                    AwakenState = "AwakenStateNotVisible"
                }
            ]);

        ZzzFullAvatarData characterDetail = await LoadTestCharacterDetailAsync("Jane_TestData.json");
        m_CharacterApiMock
            .Setup(x => x.GetCharacterDataFromIdAsync(TestLtUid, TestLToken, TestGameUid, It.IsAny<string>(), 1261u))
            .ReturnsAsync(ApiResult<ZzzFullAvatarData>.Success(characterDetail));

        m_CharacterCardServiceMock
            .Setup(x => x.GenerateCharacterCardAsync(It.IsAny<ZzzFullAvatarData>(), TestGameUid))
            .ReturnsAsync(new MemoryStream(new byte[64]));

        // Construct auth result with the same context
        AuthenticationResult authResult = AuthenticationResult.Success(m_TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        await m_Executor.OnAuthenticationCompletedAsync(authResult);

        string response = await m_DiscordTestHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("character_card.jpg").Or.Contain("Command execution completed"));
    }

    #endregion OnAuthenticationCompletedAsync Tests

    #region Helpers

    private static string CreateValidGameRecordResponse(Regions region = Regions.Asia)
    {
        string regionMapping = region switch
        {
            Regions.Asia => "prod_gf_jp",
            Regions.Europe => "prod_gf_eu",
            Regions.America => "prod_gf_usa",
            Regions.Sar => "prod_gf_sg",
            _ => "prod_gf_jp"
        };

        var gameRecord = new
        {
            retcode = 0,
            message = "OK",
            data = new
            {
                list = new[]
                {
                    new
                    {
                        game_uid = TestGameUid,
                        nickname = "TestPlayer",
                        region = regionMapping,
                        level = 70,
                        region_name = region.ToString()
                    }
                }
            }
        };

        return JsonSerializer.Serialize(gameRecord);
    }

    private void SetupDistributedCacheMock()
    {
        // Default setup for token cache - no token by default
        m_DistributedCacheMock.Setup(x =>
                x.GetAsync(It.Is<string>(key => key.StartsWith("TokenCache_")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    private void SetupHttpMessageHandlerForGameRoleApi(HttpStatusCode statusCode, string content)
    {
        HttpResponseMessage response = new()
        {
            StatusCode = statusCode,
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri != null &&
                        req.RequestUri.GetLeftPart(UriPartial.Path).ToString() ==
                        $"{HoYoLabDomains.AccountApi}/binding/api/getUserGameRolesByLtoken"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private async Task<UserModel> CreateOrUpdateTestUserAsync(
        uint profileId = 1,
        (Regions server, string gameUid)? gameProfileForZzz = null,
        Regions? lastUsedRegionForHsr = null)
    {
        Dictionary<GameName, Dictionary<string, string>>? gameUids = null;
        if (gameProfileForZzz is not null)
        {
            gameUids = new Dictionary<GameName, Dictionary<string, string>>
            {
                {
                    GameName.ZenlessZoneZero,
                    new Dictionary<string, string> { { gameProfileForZzz.Value.server.ToString(), gameProfileForZzz.Value.gameUid } }
                }
            };
        }

        Dictionary<GameName, Regions>? lastUsed = null;
        if (lastUsedRegionForHsr is not null)
        {
            lastUsed = new Dictionary<GameName, Regions>
            {
                { GameName.HonkaiStarRail, lastUsedRegionForHsr.Value }
            };
        }

        UserModel testUser = new()
        {
            Id = m_TestUserId,
            Profiles =
            [
                new()
                {
                    ProfileId = profileId,
                    LtUid = TestLtUid,
                    GameUids = gameUids,
                    LastUsedRegions = lastUsed
                }
            ]
        };

        await m_UserRepository.CreateOrUpdateUserAsync(testUser);
        return testUser;
    }

    private static async Task<ZzzFullAvatarData> LoadTestCharacterDetailAsync(string fileName)
    {
        string fullPath = Path.Combine(TestDataPath, fileName);
        string json = await File.ReadAllTextAsync(fullPath);
        ZzzFullAvatarData? data = JsonSerializer.Deserialize<ZzzFullAvatarData>(json);
        Assert.That(data, Is.Not.Null);
        return data!;
    }

    #endregion Helpers
}
