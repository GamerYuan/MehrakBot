#region

using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

public class CharacterCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ILogger<CharacterCommandModule> m_Logger;
    private readonly UserRepository m_UserRepository;
    private readonly TokenCacheService m_TokenCacheService;
    private readonly GenshinCharacterCommandService<ApplicationCommandContext> m_Service;
    private readonly CommandRateLimitService m_RateLimitService;

    public CharacterCommandModule(ILogger<CharacterCommandModule> logger, UserRepository userRepository,
        GenshinCharacterCommandService<ApplicationCommandContext> service, TokenCacheService tokenCacheService,
        CommandRateLimitService rateLimitService)
    {
        m_Logger = logger;
        m_UserRepository = userRepository;
        m_Service = service;
        m_TokenCacheService = tokenCacheService;
        m_RateLimitService = rateLimitService;
    }

    [SlashCommand("character", "Get character card",
        Contexts =
        [
            InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
        ])]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)")]
        string characterName,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        try
        {
            m_Logger.LogInformation("User {UserId} used the character command", Context.User.Id);
            if (m_RateLimitService.IsRateLimited(Context.User.Id))
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("Used command too frequent! Please try again later")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            m_RateLimitService.SetRateLimit(Context.User.Id);
            var user = await m_UserRepository.GetUserAsync(Context.User.Id);
            if (user?.Profiles == null || user.Profiles.All(x => x.ProfileId != profile))
            {
                m_Logger.LogInformation("User {UserId} does not have a profile with ID {ProfileId}",
                    Context.User.Id, profile);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("You do not have a profile with this ID")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var selectedProfile = user.Profiles.First(x => x.ProfileId == profile);
            if (selectedProfile.LastUsedRegions != null && server == null)
            {
                selectedProfile.LastUsedRegions.TryGetValue(GameName.Genshin, out var tmp);
                server = tmp;
            }

            if (server == null)
            {
                m_Logger.LogInformation("User {UserId} does not have a server selected", Context.User.Id);
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent("No cached server found. Please select a server")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            if (!m_TokenCacheService.TryGetCacheEntry(Context.User.Id, selectedProfile.LtUid, out var ltoken))
            {
                m_Logger.LogInformation("User {UserId} is not authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.Modal(AuthModalModule.AuthModal(characterName, server.Value, profile)));
            }
            else
            {
                m_Logger.LogInformation("User {UserId} is already authenticated", Context.User.Id);
                await Context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
                m_Service.Context = Context;
                await m_Service.SendCharacterCardResponseAsync(selectedProfile.LtUid, ltoken!, characterName,
                    server.Value);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error processing character command for user {UserId}", Context.User.Id);
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .WithComponents([
                    new TextDisplayProperties(
                        "An error occurred while processing your request. Please try again later.")
                ]));
        }
    }

    public static string GetHelpString()
    {
        return "## Character\n" +
               "Get character card\n" +
               "### Usage\n" +
               "```/character character [server] [profile]```\n" +
               "### Parameters\n" +
               "- `character`: Character Name (Case-insensitive)\n" +
               "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
               "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
               "### Examples\n" +
               "```/character Escoffier\n/character Traveler America\n/character Nahida Asia 3```";
    }
}
