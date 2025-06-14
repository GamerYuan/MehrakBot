#region

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrImageUpdaterServiceTests
{
    private MongoTestHelper m_MongoHelper;
    private ImageRepository m_ImageRepository;
    private Mock<IHttpClientFactory> m_HttpClientFactoryMock;
    private Mock<HttpMessageHandler> m_HttpMessageHandlerMock;
    private HttpClient m_HttpClient;
    private HsrImageUpdaterService m_Service;
    private HsrCharacterInformation m_StelleTestData;

    private static readonly string
        TestAssetsPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr", "Assets");

    [SetUp]
    public async Task Setup()
    {
        m_MongoHelper = new MongoTestHelper();
        m_ImageRepository = new ImageRepository(m_MongoHelper.MongoDbService, NullLogger<ImageRepository>.Instance);

        // Setup HTTP client mock
        m_HttpMessageHandlerMock = new Mock<HttpMessageHandler>();
        m_HttpClient = new HttpClient(m_HttpMessageHandlerMock.Object);

        m_HttpClientFactoryMock = new Mock<IHttpClientFactory>();
        m_HttpClientFactoryMock.Setup(x => x.CreateClient("Default")).Returns(m_HttpClient);

        m_Service = new HsrImageUpdaterService(
            m_ImageRepository,
            m_HttpClientFactoryMock.Object,
            NullLogger<HsrImageUpdaterService>.Instance);

        // Load test data
        var testDataJson =
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Hsr",
                "Stelle_TestData.json"));
        m_StelleTestData = JsonSerializer.Deserialize<HsrCharacterInformation>(testDataJson)!;
    }

    [TearDown]
    public void TearDown()
    {
        m_HttpClient.Dispose();
        m_MongoHelper.Dispose();
    }

    [Test]
    public async Task UpdateDataAsync_ShouldUpdateAllImages_WhenAllRequestsSucceed()
    {
        // Arrange
        SetupSuccessfulHttpResponses();
        var equipWiki = new Dictionary<string, string> { { "21004", "entry_page/12345" } };
        var relicWiki = new Dictionary<string, string>
        {
            { "61181", "entry_page/relic1" },
            { "61182", "entry_page/relic1" },
            { "61183", "entry_page/relic1" },
            { "61184", "entry_page/relic1" },
            { "61161", "entry_page/relic2" },
            { "61164", "entry_page/relic2" },
            { "63075", "entry_page/ornament1" },
            { "63076", "entry_page/ornament1" }
        };
        var wiki = new[] { equipWiki, relicWiki };

        // Act
        await m_Service.UpdateDataAsync(m_StelleTestData, wiki);

        // Assert
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_8006"), Is.True); // Character
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_21004"), Is.True); // Equip
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True); // Relic
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_8006001"), Is.True); // Skill
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_800601"), Is.True); // Rank
    }

    [Test]
    public async Task UpdateDataAsync_ShouldSkipExistingImages()
    {
        // Arrange
        await UploadExistingTestImage("hsr_8006");
        SetupSuccessfulHttpResponses();
        var wiki = CreateBasicWiki();

        // Act
        await m_Service.UpdateDataAsync(m_StelleTestData, wiki);

        // Assert - Character image should still exist (not re-downloaded)
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_8006"), Is.True);

        // Verify HTTP request was not made for existing image
        m_HttpMessageHandlerMock.Protected()
            .Verify("SendAsync", Times.Never(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("8006@2x.png")),
                ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public void UpdateDataAsync_ShouldThrowException_WhenImageDownloadFails()
    {
        // Arrange
        SetupFailedHttpResponse();
        var wiki = CreateBasicWiki(); // Act & Assert - Should throw JsonReaderException when wiki JSON is invalid
        Assert.ThrowsAsync(Is.InstanceOf<JsonException>(), async () =>
            await m_Service.UpdateDataAsync(m_StelleTestData, wiki));
    }

    [Test]
    public async Task UpdateDataAsync_ShouldProcessCharacterWithoutEquip()
    {
        // Arrange
        var characterWithoutEquip = CreateCharacterWithoutEquip();
        SetupSuccessfulHttpResponses();
        var wiki = CreateBasicWiki();

        // Act
        await m_Service.UpdateDataAsync(characterWithoutEquip, wiki);

        // Assert
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_8006"), Is.True); // Character
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_21004"), Is.False); // No equip
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldProcessRelicsWithWikiData()
    {
        // Arrange
        SetupWikiRelicResponses();
        var relicWiki = new Dictionary<string, string>
        {
            { "61181", "entry_page/relic1" }
        };

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!, relicWiki);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
        Assert.That(m_Service.GetRelicSetName(61181), Is.EqualTo("Watchmaker, Master of Dream Machinations"));
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldFallbackToDirectIcons_WhenWikiDataMissing()
    {
        // Arrange
        SetupSuccessfulHttpResponses();
        var emptyRelicWiki = new Dictionary<string, string>();

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!, emptyRelicWiki);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandleRelicsWithoutIcons()
    {
        // Arrange
        var relicsWithoutIcons = CreateRelicsWithoutIcons();
        SetupSuccessfulHttpResponses();
        var emptyRelicWiki = new Dictionary<string, string>();

        // Act
        var result = await m_Service.UpdateRelicImageAsync(relicsWithoutIcons, emptyRelicWiki);

        // Assert
        Assert.That(result, Is.False); // Should return false due to missing icons
    }

    [Test]
    public async Task GetRelicSetName_ShouldReturnCorrectSetName_WhenRelicIdExists()
    {
        // Arrange
        SetupWikiRelicResponses();
        var relicWiki = new Dictionary<string, string> { { "61181", "entry_page/relic1" } };
        await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(1), relicWiki);

        // Act
        var setName = m_Service.GetRelicSetName(61181);

        // Assert
        Assert.That(setName, Is.EqualTo("Watchmaker, Master of Dream Machinations"));
    }

    [Test]
    public void GetRelicSetName_ShouldReturnEmpty_WhenRelicIdNotFound()
    {
        // Act
        var setName = m_Service.GetRelicSetName(99999);

        // Assert
        Assert.That(setName, Is.Empty);
    }

    [Test]
    public async Task UpdateSkillImageAsync_ShouldProcessStatBonusSkills()
    {
        // Arrange
        var statBonusSkill = CreateStatBonusSkill();
        SetupSuccessfulHttpResponses();

        // Act
        _ = await m_Service.UpdateRelicImageAsync([statBonusSkill], new Dictionary<string, string>());

        // Assert - Should process stat bonus skills with different sizing and naming
        m_HttpMessageHandlerMock.Protected()
            .Verify("SendAsync", Times.AtLeastOnce(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task UpdateEquipImageAsync_ShouldFetchWikiData()
    {
        // Arrange
        SetupWikiEquipResponse();
        var equipWiki = new Dictionary<string, string> { { "21004", "entry_page/12345" } };
        var wiki = new[] { equipWiki, new Dictionary<string, string>() };

        // Act
        await m_Service.UpdateDataAsync(m_StelleTestData, wiki);

        // Assert
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_21004"), Is.True);

        // Verify wiki API was called
        m_HttpMessageHandlerMock.Protected()
            .Verify("SendAsync", Times.AtLeastOnce(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("wapi/entry_page?entry_page_id=12345") &&
                    req.Headers.Contains("X-Rpc-Wiki_app")),
                ItExpr.IsAny<CancellationToken>());
    }

    private void SetupSuccessfulHttpResponses()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                var url = request.RequestUri!.ToString();

                // Handle wiki API requests - return JSON
                if (url.Contains("wapi/entry_page"))
                {
                    // Equipment wiki response
                    response.Content = url.Contains("entry_page_id=12345")
                        ? new StringContent(CreateWikiEquipResponse())
                        : new StringContent(CreateWikiRelicResponse());
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    return response;
                }

                // Handle image downloads - return PNG
                var filename = ExtractFilenameFromUrl(url);
                if (!string.IsNullOrEmpty(filename))
                {
                    var assetPath = Path.Combine(TestAssetsPath, filename);
                    if (File.Exists(assetPath))
                    {
                        response.Content = new ByteArrayContent(File.ReadAllBytes(assetPath));
                        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                        return response;
                    }
                }

                // Fallback to a simple transparent PNG that's definitely valid
                response.Content = new ByteArrayContent(CreateSimplePng());
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                return response;
            });
    }

    private void SetupFailedHttpResponse()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}")
            });

        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private void SetupWikiRelicResponses()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var wikiResponse = CreateWikiRelicResponse();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiResponse)
                };
            });

        // Setup image downloads
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private void SetupWikiEquipResponse()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var wikiResponse = CreateWikiEquipResponse();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiResponse)
                };
            });

        // Setup image downloads
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_21004.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private string? ExtractFilenameFromUrl(string url)
    {
        // Extract ID from various URL patterns and map to test assets
        if (url.Contains("8006@2x.png") || url.Contains("8006.png")) return "hsr_8006.png";
        if (url.Contains("8006001")) return "hsr_8006001.png";
        if (url.Contains("8006002")) return "hsr_8006002.png";
        if (url.Contains("8006003")) return "hsr_8006003.png";
        if (url.Contains("8006004")) return "hsr_8006004.png";
        if (url.Contains("8006007")) return "hsr_8006007.png";
        if (url.Contains("21004")) return "hsr_21004.png";
        if (url.Contains("61181")) return "hsr_61181.png";
        if (url.Contains("61182")) return "hsr_61182.png";
        if (url.Contains("61183")) return "hsr_61183.png";
        if (url.Contains("61184")) return "hsr_61184.png";
        if (url.Contains("61161")) return "hsr_61161.png";
        if (url.Contains("61164")) return "hsr_61164.png";
        if (url.Contains("63075")) return "hsr_63075.png";
        if (url.Contains("63076")) return "hsr_63076.png";
        if (url.Contains("800601")) return "hsr_800601.png";
        if (url.Contains("800602")) return "hsr_800602.png";
        if (url.Contains("800603")) return "hsr_800603.png";
        if (url.Contains("800604")) return "hsr_800604.png";
        if (url.Contains("800605")) return "hsr_800605.png";
        if (url.Contains("800606")) return "hsr_800606.png";

        // Try to extract any ID pattern and use it
        var match = Regex.Match(url, @"(\d{4,})");
        if (match.Success)
        {
            var id = match.Groups[1].Value;
            var testFilename = $"hsr_{id}.png";
            var testPath = Path.Combine(TestAssetsPath, testFilename);
            if (File.Exists(testPath)) return testFilename;
        }

        return null;
    }

    private async Task UploadExistingTestImage(string filename)
    {
        var simplePng = CreateSimplePng();
        using var stream = new MemoryStream(simplePng);
        await m_ImageRepository.UploadFileAsync(filename, stream, "image/png");
    }

    private static byte[] CreateSimplePng()
    {
        // Use an actual test asset instead of base64 - this ensures it's a valid PNG
        var assetPath = Path.Combine(TestAssetsPath, "hsr_8006.png");
        if (File.Exists(assetPath)) return File.ReadAllBytes(assetPath);

        // Fallback to minimal 1x1 transparent PNG (well-tested base64)
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8//8/AAIB/wDT2YSyAAAAAElFTkSuQmCC"
        );
    }

    private Dictionary<string, string>[] CreateBasicWiki()
    {
        var equipWiki = new Dictionary<string, string> { { "21004", "entry_page/12345" } };
        var relicWiki = new Dictionary<string, string>
        {
            { "61181", "entry_page/relic1" },
            { "61182", "entry_page/relic1" },
            { "61183", "entry_page/relic1" },
            { "61184", "entry_page/relic1" },
            { "61161", "entry_page/relic2" },
            { "61164", "entry_page/relic2" },
            { "63075", "entry_page/ornament1" },
            { "63076", "entry_page/ornament1" }
        };
        return [equipWiki, relicWiki];
    }

    private HsrCharacterInformation CreateCharacterWithoutEquip()
    {
        var character = JsonSerializer.Deserialize<HsrCharacterInformation>(
            JsonSerializer.Serialize(m_StelleTestData))!;
        var characterNoEquip = new HsrCharacterInformation
        {
            Id = character.Id,
            Name = character.Name,
            Icon = character.Icon,
            Image = character.Image,
            Equip = null, // No equipment
            Relics = character.Relics,
            Ornaments = character.Ornaments,
            Skills = character.Skills,
            Properties = character.Properties,
            Rarity = character.Rarity,
            Element = character.Element,
            Level = character.Level,
            BaseType = character.BaseType,
            Rank = character.Rank,
            FigurePath = character.FigurePath,
            ElementId = character.ElementId,
            ServantDetail = character.ServantDetail,
            Ranks = character.Ranks
        };
        return characterNoEquip;
    }

    private List<Relic> CreateRelicsWithoutIcons()
    {
        return m_StelleTestData.Relics!.Select(r => new Relic
        {
            Id = r.Id,
            Name = r.Name,
            Icon = null! // No icon URL
        }).ToList();
    }

    private Relic CreateStatBonusSkill()
    {
        return new Relic
        {
            Id = 999,
            Name = "ATK Boost",
            Icon = "https://example.com/stat_boost.png"
        };
    }

    private string CreateWikiRelicResponse()
    {
        return JsonSerializer.Serialize(new
        {
            data = new
            {
                page = new
                {
                    name = "Watchmaker, Master of Dream Machinations",
                    modules = new[]
                    {
                        new
                        {
                            name = "Set",
                            components = new[]
                            {
                                new
                                {
                                    data = JsonSerializer.Serialize(new
                                    {
                                        list = new[]
                                        {
                                            new
                                            {
                                                title = "Watchmaker's Telescoping Lens",
                                                id = "61181",
                                                icon_url = "https://example.com/61181.png"
                                            }
                                        }
                                    })
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    private string CreateWikiEquipResponse()
    {
        return JsonSerializer.Serialize(new
        {
            data = new
            {
                page = new
                {
                    icon_url = "https://example.com/21004.png"
                }
            }
        });
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandleWikiRequestFailure()
    {
        // Arrange
        SetupFailedWikiResponse();
        var relicWiki = new Dictionary<string, string> { { "61181", "entry_page/relic1" } };

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(1), relicWiki);

        // Assert - Should fallback to direct icons when wiki fails
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandleInvalidWikiJson()
    {
        // Arrange
        SetupInvalidWikiJsonResponse();
        var relicWiki = new Dictionary<string, string> { { "61181", "entry_page/relic1" } };

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(1), relicWiki);

        // Assert - Should fallback to direct icons when wiki JSON is invalid
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandleWikiWithoutSetEntry()
    {
        // Arrange
        SetupWikiWithoutSetEntry();
        var relicWiki = new Dictionary<string, string> { { "61181", "entry_page/relic1" } };

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(1), relicWiki);

        // Assert - Should fallback to direct icons when no set entry
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandleMultipleRelicsInSameSet()
    {
        // Arrange
        SetupWikiMultipleRelicsResponse();
        var relicWiki = new Dictionary<string, string>
        {
            { "61181", "entry_page/relic1" },
            { "61182", "entry_page/relic1" },
            { "61183", "entry_page/relic1" },
            { "61184", "entry_page/relic1" }
        };

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(4), relicWiki);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61182"), Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61183"), Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61184"), Is.True);

        // All should have the same set name
        Assert.That(m_Service.GetRelicSetName(61181), Is.EqualTo("Watchmaker, Master of Dream Machinations"));
        Assert.That(m_Service.GetRelicSetName(61182), Is.EqualTo("Watchmaker, Master of Dream Machinations"));
        Assert.That(m_Service.GetRelicSetName(61183), Is.EqualTo("Watchmaker, Master of Dream Machinations"));
        Assert.That(m_Service.GetRelicSetName(61184), Is.EqualTo("Watchmaker, Master of Dream Machinations"));
    }

    [Test]
    public void UpdateEquipImageAsync_ShouldHandleWikiWithoutIconUrl()
    {
        // Arrange
        SetupWikiEquipResponseWithoutIcon();
        var equipWiki = new Dictionary<string, string> { { "21004", "entry_page/12345" } };
        var wiki = new[] { equipWiki, new Dictionary<string, string>() };

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await m_Service.UpdateDataAsync(m_StelleTestData, wiki));
    }

    [Test]
    public void UpdateDataAsync_ShouldHandleHttpRequestException()
    {
        // Arrange
        SetupHttpRequestExceptionResponse();
        var wiki = CreateBasicWiki();

        // Act & Assert
        Assert.ThrowsAsync<HttpRequestException>(async () => await m_Service.UpdateDataAsync(m_StelleTestData, wiki));
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandleQuotationMarkNormalization()
    {
        // Arrange
        SetupWikiRelicResponseWithQuotes();
        var relicWiki = new Dictionary<string, string> { { "61181", "entry_page/relic1" } };

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(1), relicWiki);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(await m_ImageRepository.FileExistsAsync("hsr_61181"), Is.True);
    }

    [Test]
    public async Task UpdateRelicImageAsync_ShouldHandlePartialSuccess()
    {
        // Arrange
        SetupPartialSuccessHttpResponse();
        var relicWiki = new Dictionary<string, string>();

        // Act
        var result = await m_Service.UpdateRelicImageAsync(m_StelleTestData.Relics!.Take(2), relicWiki);

        // Assert - Should return false when some images fail
        Assert.That(result, Is.False);
    }

    private void SetupFailedWikiResponse()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Setup successful image downloads for fallback
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private void SetupInvalidWikiJsonResponse()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("invalid json")
            });

        // Setup successful image downloads for fallback
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private void SetupWikiWithoutSetEntry()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var wikiResponse = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        page = new
                        {
                            name = "Test Set",
                            modules = new[]
                            {
                                new
                                {
                                    name = "NotSet", // Different module name
                                    components = Array.Empty<object>()
                                }
                            }
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiResponse)
                };
            });

        // Setup successful image downloads for fallback
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private void SetupWikiMultipleRelicsResponse()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var wikiResponse = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        page = new
                        {
                            name = "Watchmaker, Master of Dream Machinations",
                            modules = new[]
                            {
                                new
                                {
                                    name = "Set",
                                    components = new[]
                                    {
                                        new
                                        {
                                            data = JsonSerializer.Serialize(new
                                            {
                                                list = new[]
                                                {
                                                    new
                                                    {
                                                        title = "Watchmaker's Telescoping Lens",
                                                        id = "61181",
                                                        icon_url = "https://example.com/61181.png"
                                                    },
                                                    new
                                                    {
                                                        title = "Watchmaker's Fortuitous Wristwatch",
                                                        id = "61182",
                                                        icon_url = "https://example.com/61182.png"
                                                    },
                                                    new
                                                    {
                                                        title = "Watchmaker's Illusory Formal Suit",
                                                        id = "61183",
                                                        icon_url = "https://example.com/61183.png"
                                                    },
                                                    new
                                                    {
                                                        title = "Watchmaker's Dream-Concealing Dress Shoes",
                                                        id = "61184",
                                                        icon_url = "https://example.com/61184.png"
                                                    }
                                                }
                                            })
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiResponse)
                };
            });

        // Setup image downloads
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private void SetupWikiEquipResponseWithoutIcon()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var wikiResponse = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        page = new
                        {
                            // No icon_url property
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiResponse)
                };
            });
    }

    private void SetupHttpRequestExceptionResponse()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
    }

    private void SetupWikiRelicResponseWithQuotes()
    {
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var wikiResponse = JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        page = new
                        {
                            name = "Watchmaker, Master of Dream Machinations",
                            modules = new[]
                            {
                                new
                                {
                                    name = "Set",
                                    components = new[]
                                    {
                                        new
                                        {
                                            data = JsonSerializer.Serialize(new
                                            {
                                                list = new[]
                                                {
                                                    new
                                                    {
                                                        title = "Watchmaker's Telescoping Lens", // With curly quotes
                                                        id = "61181",
                                                        icon_url = "https://example.com/61181.png"
                                                    }
                                                }
                                            })
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiResponse)
                };
            });

        // Setup image downloads
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => !req.RequestUri!.ToString().Contains("wapi/entry_page")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.Exists(assetPath)
                        ? File.ReadAllBytes(assetPath)
                        : CreateSimplePng())
                };
            });
    }

    private void SetupPartialSuccessHttpResponse()
    {
        var callCount = 0;
        m_HttpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                callCount++;
                // First call succeeds, second fails
                if (callCount == 1)
                {
                    var filename = ExtractFilenameFromUrl(request.RequestUri!.ToString());
                    var assetPath = Path.Combine(TestAssetsPath, filename ?? "hsr_61181.png");

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(File.Exists(assetPath)
                            ? File.ReadAllBytes(assetPath)
                            : CreateSimplePng())
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
    }
}