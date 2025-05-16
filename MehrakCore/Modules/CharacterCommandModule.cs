#region

using System.Globalization;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Services.Genshin;

public class CharacterCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ILogger<CharacterCommandModule> m_Logger;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterCommandService<ApplicationCommandContext> m_Service;
    private readonly CommandRateLimitService m_RateLimitService;
    private readonly CommandLocalizerService m_LocalizerService;

    public CharacterCommandModule(ILogger<CharacterCommandModule> logger,
        GenshinCharacterCommandService<ApplicationCommandContext> service, TokenCacheService tokenCacheService,
        CommandRateLimitService rateLimitService, CommandLocalizerService localizerService)
    {
        m_Logger = logger;
        m_Service = service;
        m_TokenCacheService = tokenCacheService;
        m_RateLimitService = rateLimitService;
        m_LocalizerService = localizerService;
    }

    [SlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions server,
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)")]
        string characterName)
    {
        var userLocale = new CultureInfo(Context.Interaction.UserLocale);
        try
        {
            m_Logger.LogInformation("User {UserId} used the character command, locale: {Locale}", Context.User.Id,
                Context.Interaction.UserLocale);
            if (m_RateLimitService.IsRateLimited(Context.User.Id))
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties()
                        .WithContent(m_LocalizerService.GetLocalizedString("RateLimitErrorMessage", userLocale))
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            m_RateLimitService.SetRateLimit(Context.User.Id);

            if (!m_TokenCacheService.TryGetToken(Context.User.Id, out _) ||
                !m_TokenCacheService.TryGetLtUid(Context.User.Id, out _))
            {
                m_Logger.LogInformation("User {UserId} is not authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.Modal(AuthModalModule.AuthModal(server, characterName,
                        m_LocalizerService.GetLocalizedString("AuthModalTitle", userLocale),
                        m_LocalizerService.GetLocalizedString("AuthModalPassphraseLabel", userLocale))));
            }
            else
            {
                m_Logger.LogInformation("User {UserId} is already authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                m_Service.Context = Context;
                await m_Service.SendCharacterCardResponseAsync(server, characterName);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing character command for user {UserId}", Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        m_LocalizerService.GetLocalizedString("UnknownErrorMessage", userLocale))
                ]));
        }
    }

    public class RegionAutoCompleteProvider : IAutocompleteProvider<AutocompleteInteractionContext>
    {
        private static readonly List<string> Regions = ["Asia", "Europe", "America", "SAR"];

        public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
            ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
        {
            var choices = Regions
                .Where(x => x.Contains(option.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Select(x => new ApplicationCommandOptionChoiceProperties(x, x));

            return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(choices);
        }
    }
}
