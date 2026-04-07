using Mehrak.Bot.Services;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Modules.Common;

public class HylEmbedModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly IBotService m_PostService;
    private readonly ILogger<HylEmbedModule> m_Logger;

    public HylEmbedModule(
        [FromKeyedServices(nameof(HylEmbedService))] IBotService postService,
        ILogger<HylEmbedModule> logger)
    {
        m_PostService = postService;
        m_Logger = logger;
    }

    [SlashCommand("hyl", "Embeds a HoYoLAB post",
        Contexts = [InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel])]
    public async Task EmbedPostAsync(
        [SlashCommandParameter(Name = "url", Description = "The URL of the HoYoLAB post to embed")]
        string url,
        [SlashCommandParameter(Name = "language", Description = "The display language of the embedded post (Defaults to English)")]
        WikiLocales language = WikiLocales.EN)
    {
        var sanitisedUrl = url.Trim().Trim('"').ReplaceLineEndings("");
        try
        {
            m_Logger.LogInformation("User {UserId} is embedding HoYoLAB post with URL {Url}", Context.User.Id, sanitisedUrl);

            if (!Uri.TryCreate(sanitisedUrl, UriKind.RelativeOrAbsolute, out var uri))
            {
                m_Logger.LogWarning("Failed to parse post ID from URL {Url} for user {UserId}", sanitisedUrl, Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                    .AddComponents(new TextDisplayProperties("The provided URL is invalid. Please ensure it is a valid HoYoLAB post URL."))));
                return;
            }

            if (!long.TryParse(uri.Segments.LastOrDefault(), out var postId))
            {
                m_Logger.LogWarning("Failed to parse post ID from URL {Url} for user {UserId}", sanitisedUrl, Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                    .AddComponents(new TextDisplayProperties("The provided URL is invalid. Please ensure it is a valid HoYoLAB post URL."))));
                return;
            }

            var context = new BotContext(Context, ("locale", language), ("postId", postId));
            m_PostService.Context = context;

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            await m_PostService.ExecuteAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while attempting to embed HoYoLAB post with URL {Url} for user {UserId}", sanitisedUrl, Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties("An unexpected error occurred while embedding the HoYoLAB post. Please try again later.")));
        }
    }
}
