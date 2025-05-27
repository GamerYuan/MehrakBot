#region

using MehrakCore.Services.Commands;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

[SlashCommand("genshin", "Genshin Toolbox",
    Contexts =
    [
        InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
    ])]
public class GenshinCommandModule : ApplicationCommandModule<ApplicationCommandContext>, ICommandModule
{
    private readonly ILogger<GenshinCommandModule> m_Logger;
    private readonly ICharacterCommandService<GenshinCommandModule> m_CharacterCommandService;
    private readonly CommandRateLimitService m_CommandRateLimitService;

    public GenshinCommandModule(ICharacterCommandService<GenshinCommandModule> characterCommandService,
        CommandRateLimitService commandRateLimitService, ILogger<GenshinCommandModule> logger)
    {
        m_Logger = logger;
        m_CharacterCommandService = characterCommandService;
        m_CommandRateLimitService = commandRateLimitService;
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)")]
        string characterName,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the character command with character {CharacterName}, server {Server}, profile {ProfileId}",
            Context.User.Id, characterName, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        if (string.IsNullOrWhiteSpace(characterName))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Character name cannot be empty")
                    .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        m_CharacterCommandService.Context = Context;
        await m_CharacterCommandService.ExecuteAsync(characterName, server, profile);
    }

    private async Task<bool> ValidateRateLimitAsync()
    {
        if (await m_CommandRateLimitService.IsRateLimitedAsync(Context.Interaction.User.Id))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Used command too frequent! Please try again later")
                    .WithFlags(MessageFlags.Ephemeral)));
            return false;
        }

        await m_CommandRateLimitService.SetRateLimitAsync(Context.Interaction.User.Id);
        return true;
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
