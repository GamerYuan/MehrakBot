#region

using MehrakCore.Provider.Commands.Hsr;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

[SlashCommand("hsr", "Honkai: Star Rail Toolbox")]
public class HsrCommandModule : ApplicationCommandModule<ApplicationCommandContext>, ICommandModule
{
    public ICharacterCommandExecutor<HsrCommandModule> CharacterCommandExecutor { get; }
    private readonly CommandRateLimitService m_CommandRateLimitService;
    private readonly ILogger<HsrCommandModule> m_Logger;

    public HsrCommandModule(ICharacterCommandExecutor<HsrCommandModule> characterCommandExecutor,
        CommandRateLimitService commandRateLimitService, ILogger<HsrCommandModule> logger)
    {
        CharacterCommandExecutor = characterCommandExecutor;
        m_CommandRateLimitService = commandRateLimitService;
        m_Logger = logger;
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)",
            AutocompleteProviderType = typeof(HsrCharacterAutocompleteProvider))]
        string characterName,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        if (!await ValidateRateLimitAsync()) return;

        if (string.IsNullOrWhiteSpace(characterName))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Character name cannot be empty")
                    .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        CharacterCommandExecutor.Context = Context;
        await CharacterCommandExecutor.ExecuteAsync(characterName, server, profile);
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

    public static string GetHelpString(string subcommand = "")
    {
        return subcommand switch
        {
            "character" => "## HSR Character\n" +
                           "Get character card from Honkai: Star Rail\n" +
                           "### Usage\n" +
                           "```/hsr character <character> [server] [profile]```\n" +
                           "### Parameters\n" +
                           "- `character`: Character Name (Case-insensitive)\n" +
                           "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                           "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                           "### Examples\n" +
                           "```/hsr character Trailblazer\n/hsr character Acheron America\n/hsr character Tribbie Asia 3```",
            _ => "## Honkai: Star Rail Toolbox\n" +
                 "Honkai: Star Rail related commands and utilities.\n" +
                 "### Subcommands\n" +
                 "- `character`: Get character card from Honkai: Star Rail\n"
        };
    }
}
