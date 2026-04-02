using System.Text.Json;
using System.Text.RegularExpressions;
using Mehrak.Bot.Models;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace Mehrak.Bot.Services;

internal class HylEmbedService : IBotService
{
    private const int ComponentLimit = 10;
    private const int PostLengthLimit = 1000;

    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownEscapeRegex = new(@"([\\`*_{}\[\]()#+\-.!|>~])", RegexOptions.Compiled);

    private readonly IApiService<HylPost, HylPostApiContext> m_ApiService;
    private readonly IBotLocalizationService m_LocalizationService;
    private readonly ILogger<HylEmbedService> m_Logger;

    public IBotContext? Context { get; set; }

    public HylEmbedService(
        IApiService<HylPost, HylPostApiContext> apiService,
        IBotLocalizationService localizationService,
        ILogger<HylEmbedService> logger)
    {
        m_ApiService = apiService;
        m_LocalizationService = localizationService;
        m_Logger = logger;
    }

    public async Task ExecuteAsync()
    {
        if (Context == null) throw new InvalidOperationException("Context must be set before executing the service.");

        var postId = Context.GetParameter<long>("postId");
        var locale = Context.GetParameter<WikiLocales>("locale");

        m_Logger.LogInformation("Fetching HoYoLAB post {PostId} for user {UserId} with locale {Locale}",
            postId,
            Context.DiscordContext.Interaction.User.Id,
            locale);

        var response = await m_ApiService.GetAsync(new HylPostApiContext(Context.DiscordContext.Interaction.User.Id, postId, locale));
        if (!response.IsSuccess)
        {
            m_Logger.LogWarning("Failed to fetch HoYoLAB post {PostId}. Error: {ErrorMessage}", postId, response.ErrorMessage);
            await Context.DiscordContext.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties($"Failed to retrieve HoYoLAB post: {response.ErrorMessage}")));
            return;
        }

        var post = response.Data;
        m_Logger.LogDebug("Successfully fetched post {PostId}. Structured content length: {Length}",
            post.Post.PostId,
            post.Post.StructuredContent.Length);

        var inserts = JsonSerializer.Deserialize<List<HylPostStructuredInsert>>(post.Post.StructuredContent);

        if (inserts == null)
        {
            m_Logger.LogWarning("Failed to parse structured content for post {PostId}", post.Post.PostId);
            await Context.DiscordContext.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("Failed to parse HoYoLAB post content.")));
            return;
        }

        m_Logger.LogDebug("Parsed {InsertCount} structured inserts for post {PostId}", inserts.Count, post.Post.PostId);

        var footer = post.Post.OriginLang != locale.ToLocaleString()
            ? $"-# HoYoLAB · {m_LocalizationService.Get(locale, "Footer")} · <t:{post.Post.CreatedAt}:F>"
            : $"-# HoYoLAB · <t:{post.Post.CreatedAt}:F>";

        var container = new ComponentContainerProperties()
            .AddComponents(new TextDisplayProperties($"## [{post.Post.Subject}](https://www.hoyolab.com/article/{post.Post.PostId})"));

        var components = BuildComponents(inserts, locale, post.Post.PostId);
        m_Logger.LogInformation("Built {ComponentCount} components for post {PostId}", components.Count, post.Post.PostId);

        container.AddComponents(components);

        if (post.CoverList.Count > 0)
        {
            container.AddComponents(new MediaGalleryProperties(
                [new MediaGalleryItemProperties(new ComponentMediaProperties(post.CoverList[0].Url))]));
        }
        else if (post.ImageList.Count > 0)
        {
            container.AddComponents(new MediaGalleryProperties(
                [new MediaGalleryItemProperties(new ComponentMediaProperties(post.ImageList[0].Url))]));
        }

        container.AddComponents(new TextDisplayProperties(footer));

        m_Logger.LogInformation("Sending follow-up message with components for post {PostId}", post.Post.PostId);
        await Context.DiscordContext.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithFlags(MessageFlags.IsComponentsV2)
            .AddComponents([container]));
    }

    private List<IComponentContainerComponentProperties>
        BuildComponents(IEnumerable<HylPostStructuredInsert> elements, WikiLocales locale, string postId)
    {
        var components = new List<IComponentContainerComponentProperties>();
        var currentLength = 0;
        var orderedListIndex = 1;

        var detailString = m_LocalizationService.Get(locale, "Details");
        var videoLabel = m_LocalizationService.Get(locale, "Video");
        var videoButton = m_LocalizationService.Get(locale, "VideoButton");
        var articleButton = m_LocalizationService.Get(locale, "ArticleButton");
        var voteButton = m_LocalizationService.Get(locale, "VoteButton");
        var articleUrl = $"https://www.hoyolab.com/article/{postId}";

        foreach (var element in elements)
        {
            if (components.Count >= ComponentLimit)
                break;

            if (element.Insert is null)
                continue;

            var insert = element.Insert;

            if (element.Attributes?.List == "ordered" && insert.Text == "\n")
            {
                currentLength += PrefixClosestLineWithOrderedList(components, orderedListIndex++);
                AppendOrCreateTextDisplay(components, insert.Text);
                currentLength += insert.Text.Length;
                continue;
            }

            if (element.Attributes?.Link is not null)
            {
                BuildLinkText(components, element.Attributes.Link, insert.Text, ref currentLength);
            }
            else if (insert.Text is not null)
            {
                var formattedText = FormatText(insert.Text);
                AppendOrCreateTextDisplay(components, formattedText);
                currentLength += formattedText.Length;
            }
            else if (insert.Card?.CardGroup?.ArticleCards is { Count: > 0 } articleCards)
            {
                for (var i = 0; i < articleCards.Count && i < 3 && components.Count < ComponentLimit; i++)
                {
                    var articleCard = articleCards[i];
                    var title = articleCard.Info.Title;
                    var nickname = articleCard.User.Nickname;
                    var content = $"{title}\n-# {nickname}";
                    var section = new ComponentSectionProperties(
                        new LinkButtonProperties(articleButton, $"https://www.hoyolab.com/article/{articleCard.Meta.MetaId}"),
                        [new TextDisplayProperties(content)]);
                    components.Add(section);
                    currentLength += title.Length + 3 + nickname.Length;
                }
            }
            else if (insert.Video is { } video)
            {
                var section = new ComponentSectionProperties(
                    new LinkButtonProperties(videoButton, UrlRegex.IsMatch(video.Video) ? video.Video : articleUrl),
                    [new TextDisplayProperties($"[{videoLabel}]")]);
                components.Add(section);
                currentLength += videoLabel.Length + 2;
            }
            else if (insert.Vote?.Vote is { } vote)
            {
                var section = new ComponentSectionProperties(
                    new LinkButtonProperties(voteButton, articleUrl),
                    [new TextDisplayProperties(vote.Title)]);
                components.Add(section);
                currentLength += vote.Title.Length;
            }

            if (currentLength < PostLengthLimit)
                continue;

            m_Logger.LogDebug("Post content reached length limit ({Length}/{Limit}) for post {PostId}",
                currentLength,
                PostLengthLimit,
                postId);

            if (components.LastOrDefault() is TextDisplayProperties text)
            {
                var lastBoundary = PostLengthLimit - (currentLength - text.Content.Length);
                while (lastBoundary > 0 && text.Content[lastBoundary] != ' ')
                    lastBoundary--;

                if (lastBoundary <= 0)
                    lastBoundary = Math.Min(text.Content.Length, PostLengthLimit);

                text.Content = $"{text.Content[..lastBoundary]}...\n\n{detailString}";
            }
            else
            {
                components.Add(new TextDisplayProperties($"\n\n{detailString}"));
            }

            break;
        }

        return components;
    }

    private static void BuildLinkText(List<IComponentContainerComponentProperties> components, string link, string? insertText, ref int currentLength)
    {
        if (string.IsNullOrWhiteSpace(insertText))
            return;

        var trimmedLink = link.Trim();
        var trimmedText = insertText.Trim();
        string content;

        if (trimmedLink == trimmedText)
            content = insertText;
        else if (UrlRegex.IsMatch(trimmedText))
            content = trimmedLink;
        else
            content = $"[{insertText}]({trimmedLink})";

        AppendOrCreateTextDisplay(components, content);
        currentLength += content.Length;
    }

    private static void AppendOrCreateTextDisplay(List<IComponentContainerComponentProperties> components, string content)
    {
        if (components.LastOrDefault() is TextDisplayProperties text)
        {
            text.Content += content;
            return;
        }

        components.Add(new TextDisplayProperties(content));
    }

    private static int PrefixClosestLineWithOrderedList(List<IComponentContainerComponentProperties> components, int orderedListIndex)
    {
        for (var i = components.Count - 1; i >= 0; i--)
        {
            if (components[i] is not TextDisplayProperties text)
                continue;

            var lineStart = text.Content.LastIndexOf('\n');
            lineStart = lineStart >= 0 ? lineStart + 1 : 0;

            var prefix = $"{orderedListIndex}. ";
            text.Content = text.Content.Insert(lineStart, prefix);
            return prefix.Length;
        }

        return 0;
    }

    private static string FormatText(string input)
    {
        var matches = UrlRegex.Matches(input);
        if (matches.Count == 0)
            return EscapeMarkdown(input);

        var result = new System.Text.StringBuilder();
        var lastIndex = 0;
        foreach (Match match in matches)
        {
            result.Append(EscapeMarkdown(input[lastIndex..match.Index]));
            result.Append(match.Value);
            lastIndex = match.Index + match.Length;
        }

        result.Append(EscapeMarkdown(input[lastIndex..]));
        return result.ToString();
    }

    private static string EscapeMarkdown(string input)
    {
        return MarkdownEscapeRegex.Replace(input, "\\$1");
    }
}
