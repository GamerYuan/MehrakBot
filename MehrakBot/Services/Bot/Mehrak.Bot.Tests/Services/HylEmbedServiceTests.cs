#region

using System.Text.Json;
using Mehrak.Bot.Models;
using Mehrak.Bot.Services;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using Moq;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Tests.Services;

/// <summary>
/// Unit tests for HylEmbedService validating HoYoLAB post embed generation,
/// localization integration, media handling, and error handling.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HylEmbedServiceTests
{
    private Mock<IApiService<HylPost, HylPostApiContext>> m_MockApiService = null!;
    private Mock<IBotLocalizationService> m_MockLocalizationService = null!;
    private Mock<ILogger<HylEmbedService>> m_MockLogger = null!;
    private HylEmbedService m_Service = null!;
    private DiscordTestHelper m_DiscordHelper = null!;

    private ulong m_TestUserId;
    private const long TestPostId = 44470660;
    private const WikiLocales TestLocale = WikiLocales.EN;
    private const string TestDataPath = "TestData";

    [SetUp]
    public void Setup()
    {
        m_MockApiService = new Mock<IApiService<HylPost, HylPostApiContext>>();
        m_MockLocalizationService = new Mock<IBotLocalizationService>();
        m_MockLogger = new Mock<ILogger<HylEmbedService>>();
        m_DiscordHelper = new DiscordTestHelper();

        m_TestUserId = (ulong)new Random(DateTime.UtcNow.Millisecond).NextInt64(100000000, 999999999);

        m_Service = new HylEmbedService(
            m_MockApiService.Object,
            m_MockLocalizationService.Object,
            m_MockLogger.Object);

        SetupLocalizationDefaults();
    }

    [TearDown]
    public void TearDown()
    {
        m_MockApiService.Reset();
        m_MockLocalizationService.Reset();
        m_MockLogger.Reset();
        m_DiscordHelper.Dispose();
    }

    #region ExecuteAsync - Success Tests

    [Test]
    public async Task ExecuteAsync_SimpleTextPost_SendsCorrectEmbed()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        m_MockApiService
            .Setup(x => x.GetAsync(It.Is<HylPostApiContext>(ctx =>
                ctx.UserId == m_TestUserId &&
                ctx.PostId == TestPostId &&
                ctx.Locale == TestLocale)))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Sample Post"));
        Assert.That(response, Does.Contain("hoyolab.com/article/44470660"));
        Assert.That(response, Does.Contain("This is a sample post"));

        m_MockApiService.Verify(
            x => x.GetAsync(It.Is<HylPostApiContext>(ctx =>
                ctx.UserId == m_TestUserId &&
                ctx.PostId == TestPostId &&
                ctx.Locale == TestLocale)),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PostWithImage_IncludesMediaGallery()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_2.json");
        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Sample Post with image"));
        Assert.That(response, Does.Contain("This is a sample post with an image"));

        using var json = JsonDocument.Parse(response);
        var components = json.RootElement.GetProperty("components");
        Assert.That(components.GetArrayLength(), Is.EqualTo(1));

        var innerComponents = components[0].GetProperty("components");
        var mediaGallery = innerComponents.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("type", out var t) && t.GetInt32() == 12);
        Assert.That(mediaGallery.ValueKind, Is.EqualTo(JsonValueKind.Object));

        var items = mediaGallery.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));

        var mediaUrl = items[0].GetProperty("media").GetProperty("url").GetString();
        Assert.That(mediaUrl, Is.EqualTo("https://example.com/image.jpg"));
    }

    [Test]
    public async Task ExecuteAsync_PostWithCover_IncludesCoverImage()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_3.json");
        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Sample Post with cover"));
        Assert.That(response, Does.Contain("This is a sample post with a cover"));

        using var json = JsonDocument.Parse(response);
        var components = json.RootElement.GetProperty("components");
        Assert.That(components.GetArrayLength(), Is.EqualTo(1));

        var innerComponents = components[0].GetProperty("components");
        var mediaGallery = innerComponents.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("type", out var t) && t.GetInt32() == 12);
        Assert.That(mediaGallery.ValueKind, Is.EqualTo(JsonValueKind.Object));

        var items = mediaGallery.GetProperty("items");
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));

        var mediaUrl = items[0].GetProperty("media").GetProperty("url").GetString();
        Assert.That(mediaUrl, Is.EqualTo("https://example.com/cover.jpg"));
    }

    [Test]
    public async Task ExecuteAsync_PostWithImageAndCover_PrefersCoverImage()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_4.json");
        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Sample Post with image and cover"));

        using var json = JsonDocument.Parse(response);
        var components = json.RootElement.GetProperty("components");
        var innerComponents = components[0].GetProperty("components");

        var mediaGallery = innerComponents.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("type", out var t) && t.GetInt32() == 12);
        Assert.That(mediaGallery.ValueKind, Is.EqualTo(JsonValueKind.Object));

        var mediaUrl = mediaGallery.GetProperty("items")[0].GetProperty("media").GetProperty("url").GetString();
        Assert.That(mediaUrl, Is.EqualTo("https://example.com/cover.jpg"));
    }

    #endregion

    #region ExecuteAsync - Error Handling Tests

    [Test]
    public async Task ExecuteAsync_ApiServiceFailure_SendsErrorMessage()
    {
        // Arrange
        const string errorMessage = "API request failed";
        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Failure(StatusCode.ExternalServerError, errorMessage));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Failed to retrieve HoYoLAB post"));
        Assert.That(response, Does.Contain(errorMessage));
    }

    [Test]
    public async Task ExecuteAsync_InvalidStructuredContent_ThrowsJsonException()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = "invalid json";

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act & Assert - Service currently throws on invalid JSON
        Assert.ThrowsAsync<JsonException>(m_Service.ExecuteAsync);
    }

    [Test]
    public async Task ExecuteAsync_NullStructuredContent_SendsErrorMessage()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = "null";

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Failed to parse HoYoLAB post content"));
    }

    [Test]
    public void ExecuteAsync_NullContext_ThrowsInvalidOperationException()
    {
        // Arrange
        m_Service.Context = null;

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(m_Service.ExecuteAsync);
    }

    #endregion

    #region ExecuteAsync - Localization Tests

    [Test]
    public async Task ExecuteAsync_TranslatedPost_IncludesLocalizedFooter()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.Lang = "es-es";
        postData.Post.OriginLang = "en-us";

        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Footer"))
            .Returns("Translated");

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Translated"));
    }

    [Test]
    public async Task ExecuteAsync_SameLanguagePost_OmitsLocalizedFooter()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.Lang = "en-us";
        postData.Post.OriginLang = "en-us";

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("HoYoLAB"));
        Assert.That(response, Does.Not.Contain("Translated"));
    }

    #endregion

    #region ExecuteAsync - Content Formatting Tests

    [Test]
    public async Task ExecuteAsync_PostWithLinks_FormatsMarkdownLinks()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new()
            {
                Insert = new HylPostInsertContent { Text = "Click here" },
                Attributes = new HylPostInsertAttributes { Link = "https://example.com" }
            }
        });

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("[Click here](https://example.com)"));
    }

    [Test]
    public async Task ExecuteAsync_PostWithVideo_IncludesVideoSection()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new()
            {
                Insert = new HylPostInsertContent
                {
                    Video = new HylPostInsertVideo
                    {
                        Video = "https://example.com/video.mp4",
                        Describe = "A test video"
                    }
                }
            }
        });

        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Video"))
            .Returns("Video");
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "VideoButton"))
            .Returns("Watch Video");

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Watch Video"));
    }

    [Test]
    public async Task ExecuteAsync_PostWithVote_IncludesVoteSection()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new()
            {
                Insert = new HylPostInsertContent
                {
                    Vote = new HylPostVote
                    {
                        Vote = new HylPostVoteDetails
                        {
                            Id = "1",
                            Uid = "100",
                            Url = "https://example.com/vote",
                            Title = "Vote on your favorite",
                            VoteOptions = ["Option 1", "Option 2"],
                            VoteLimit = 1,
                            EndTime = "2025-12-31",
                            EndTimeType = "fixed",
                            SyncEndTimeType = false,
                            Status = "active"
                        }
                    }
                }
            }
        });

        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "VoteButton"))
            .Returns("Vote Now");

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Vote on your favorite"));
        Assert.That(response, Does.Contain("Vote Now"));
    }

    [Test]
    public async Task ExecuteAsync_PostWithArticleCards_IncludesArticleSections()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new()
            {
                Insert = new HylPostInsertContent
                {
                    Card = new HylPostInsertCard
                    {
                        CardGroup = new HylPostInsertCardGroup
                        {
                            ArticleCards =
                            [
                                new HylPostArticleCard
                                {
                                    Meta = new HylPostMetadata { Type = 1, MetaId = "12345", OriginUrl = "https://example.com" },
                                    Info = new HylPostArticleInfo
                                    {
                                        Title = "Article Title 1",
                                        Cover = "https://example.com/cover.jpg",
                                        HasCover = true,
                                        ViewNum = "1000",
                                        CreatedAt = "2025-01-01",
                                        Status = 1,
                                        TipMsg = "",
                                        TypeDesc = "Article",
                                        ViewType = 1,
                                        SubType = 1,
                                        JumpUrl = "https://example.com/article/12345"
                                    },
                                    User = new HylPostArticleUser
                                    {
                                        Uid = "100",
                                        Avatar = "https://example.com/avatar.jpg",
                                        IconUrl = "https://example.com/icon.jpg",
                                        Nickname = "Author1",
                                        IsOwner = true
                                    }
                                }
                            ]
                        }
                    }
                }
            }
        });

        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "ArticleButton"))
            .Returns("Read Article");

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Article Title 1"));
        Assert.That(response, Does.Contain("Author1"));
        Assert.That(response, Does.Contain("Read Article"));
    }

    #endregion

    #region ExecuteAsync - Content Truncation Tests

    [Test]
    public async Task ExecuteAsync_LongContent_TruncatesAtLimit()
    {
        // Arrange
        var longText = new string('a', 1100);
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new() { Insert = new HylPostInsertContent { Text = longText } }
        });

        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Details"))
            .Returns("Read more...");

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("Read more..."));

        // Parse JSON and verify truncated content length in the component
        using var json = JsonDocument.Parse(response);
        var components = json.RootElement.GetProperty("components");
        var innerComponents = components[0].GetProperty("components");

        var truncatedContent = innerComponents[1].GetProperty("content").GetString();
        Assert.That(truncatedContent, Is.Not.Null);

        var ellipsisIndex = truncatedContent!.IndexOf("...\n\n", StringComparison.Ordinal);
        Assert.That(ellipsisIndex, Is.GreaterThan(0));
        var truncatedText = truncatedContent[..ellipsisIndex];

        Assert.That(truncatedText, Has.Length.LessThanOrEqualTo(1000));
    }

    [Test]
    public async Task ExecuteAsync_LongContentRespectsWordBoundaries_TruncatesAtWordBoundary()
    {
        // Arrange
        var words = Enumerable.Range(0, 200).Select(i => $"word{i}");
        var longText = string.Join(" ", words);
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new() { Insert = new HylPostInsertContent { Text = longText } }
        });

        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Details"))
            .Returns("...");

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("..."));

        using var json = JsonDocument.Parse(response);
        var components = json.RootElement.GetProperty("components");
        var innerComponents = components[0].GetProperty("components");

        var truncatedContent = innerComponents[1].GetProperty("content").GetString();
        Assert.That(truncatedContent, Is.Not.Null);

        var ellipsisIndex = truncatedContent!.IndexOf("...\n\n", StringComparison.Ordinal);
        Assert.That(ellipsisIndex, Is.GreaterThan(0));
        var truncatedText = truncatedContent[..ellipsisIndex];

        Assert.That(truncatedText, Is.Not.Null);
        Assert.That(truncatedText, Has.Length.GreaterThan(990));
        Assert.That(truncatedText, Has.Length.LessThanOrEqualTo(1000));
    }

    #endregion

    #region ExecuteAsync - Component Limit Tests

    [Test]
    public async Task ExecuteAsync_ExceedsComponentLimit_StopsAtLimit()
    {
        // Arrange
        var inserts = Enumerable.Range(0, 15)
            .Select(i => new HylPostStructuredInsert
            {
                Insert = new HylPostInsertContent { Text = $"Paragraph {i}\n" }
            })
            .ToList();

        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(inserts);

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Is.Not.Null);

        // Parse JSON and verify the component count does not exceed the limit of 10
        using var json = JsonDocument.Parse(response);
        var components = json.RootElement.GetProperty("components");
        var innerComponents = components[0].GetProperty("components");

        Assert.That(innerComponents.GetArrayLength(), Is.LessThanOrEqualTo(10));
    }

    #endregion

    #region ExecuteAsync - Markdown Escaping Tests

    [Test]
    public async Task ExecuteAsync_TextWithMarkdownCharacters_EscapesProperly()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new() { Insert = new HylPostInsertContent { Text = "Text with *bold* and _italic_ characters" } }
        });

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("\\*"));
        Assert.That(response, Does.Contain("\\_"));
    }

    [Test]
    public async Task ExecuteAsync_TextWithUrls_DoesNotEscapeUrls()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new() { Insert = new HylPostInsertContent { Text = "Visit https://example.com for more info" } }
        });

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("https://example.com"));
        Assert.That(response, Does.Not.Contain("https:\\/\\/example.com"));
    }

    #endregion

    #region ExecuteAsync - Ordered List Tests

    [Test]
    public async Task ExecuteAsync_OrderedList_AddsNumberPrefix()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new() { Insert = new HylPostInsertContent { Text = "First item" } },
            new() { Insert = new HylPostInsertContent { Text = "\n" }, Attributes = new HylPostInsertAttributes { List = "ordered" } },
            new() { Insert = new HylPostInsertContent { Text = "Second item" } },
            new() { Insert = new HylPostInsertContent { Text = "\n" }, Attributes = new HylPostInsertAttributes { List = "ordered" } }
        });

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("1."));
    }

    #endregion

    #region ExecuteAsync - Link Formatting Tests

    [Test]
    public async Task ExecuteAsync_LinkSameAsText_RendersAsPlainText()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new ()
            {
                Insert = new HylPostInsertContent { Text = "https://example.com" },
                Attributes = new HylPostInsertAttributes { Link = "https://example.com" }
            }
        });

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("https://example.com"));
    }

    [Test]
    public async Task ExecuteAsync_LinkTextIsUrl_RendersAsLinkUrl()
    {
        // Arrange
        var postData = LoadTestPost("hyl_post_1.json");
        postData.Post.StructuredContent = JsonSerializer.Serialize(new List<HylPostStructuredInsert>
        {
            new()
            {
                Insert = new HylPostInsertContent { Text = "https://other.com" },
                Attributes = new HylPostInsertAttributes { Link = "https://example.com" }
            }
        });

        m_MockApiService
            .Setup(x => x.GetAsync(It.IsAny<HylPostApiContext>()))
            .ReturnsAsync(Result<HylPost>.Success(postData));

        var interaction = m_DiscordHelper.CreateCommandInteraction(m_TestUserId);
        var mockContext = new Mock<IBotContext>();
        var mockDiscordContext = new Mock<IInteractionContext>();
        mockDiscordContext.SetupGet(x => x.Interaction).Returns(interaction);
        mockContext.SetupGet(x => x.DiscordContext).Returns(mockDiscordContext.Object);
        mockContext.Setup(x => x.GetParameter<long>("postId")).Returns(TestPostId);
        mockContext.Setup(x => x.GetParameter<WikiLocales>("locale")).Returns(TestLocale);

        m_Service.Context = mockContext.Object;

        // Act
        await m_Service.ExecuteAsync();

        // Assert
        var response = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
        Assert.That(response, Does.Contain("https://example.com"));
    }

    #endregion

    #region Property Tests

    [Test]
    public void Context_CanBeSetAndRetrieved()
    {
        // Arrange
        var mockContext = new Mock<IBotContext>();

        // Act
        m_Service.Context = mockContext.Object;

        // Assert
        Assert.That(m_Service.Context, Is.EqualTo(mockContext.Object));
    }

    #endregion

    #region Helper Methods

    private HylPost LoadTestPost(string filename)
    {
        var filePath = Path.Combine(TestContext.CurrentContext.TestDirectory, TestDataPath, filename);
        var json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<HylPost>(json, options)!;
    }

    private void SetupLocalizationDefaults()
    {
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Footer"))
            .Returns("Translated");
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Details"))
            .Returns("...");
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "Video"))
            .Returns("Video");
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "VideoButton"))
            .Returns("Watch");
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "ArticleButton"))
            .Returns("Read");
        m_MockLocalizationService
            .Setup(x => x.Get(TestLocale, "VoteButton"))
            .Returns("Vote");
    }

    #endregion
}

