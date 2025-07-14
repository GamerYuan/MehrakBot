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
    private readonly ICharacterCommandExecutor<HsrCommandModule> m_CharacterCommandExecutor;
    private readonly IRealTimeNotesCommandExecutor<HsrCommandModule> m_RealTimeNotesCommandExecutor;
    private readonly ICodeRedeemExecutor<HsrCommandModule> m_CodesRedeemExecutor;
    private readonly CommandRateLimitService m_CommandRateLimitService;
    private readonly ILogger<HsrCommandModule> m_Logger;

    public HsrCommandModule(ICharacterCommandExecutor<HsrCommandModule> characterCommandExecutor,
        IRealTimeNotesCommandExecutor<HsrCommandModule> realTimeNotesCommandExecutor,
        CommandRateLimitService commandRateLimitService, ILogger<HsrCommandModule> logger,
        ICodeRedeemExecutor<HsrCommandModule> codesRedeemExecutor)
    {
        m_CharacterCommandExecutor = characterCommandExecutor;
        m_RealTimeNotesCommandExecutor = realTimeNotesCommandExecutor;
        m_CommandRateLimitService = commandRateLimitService;
        m_Logger = logger;
        m_CodesRedeemExecutor = codesRedeemExecutor;
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

        m_CharacterCommandExecutor.Context = Context;
        await m_CharacterCommandExecutor.ExecuteAsync(characterName, server, profile);
    }

    [SubSlashCommand("notes", "Get real-time notes")]
    public async Task RealTimeNotesCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        if (!await ValidateRateLimitAsync()) return;

        m_RealTimeNotesCommandExecutor.Context = Context;
        await m_RealTimeNotesCommandExecutor.ExecuteAsync(server, profile);
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

    [SubSlashCommand("codes", "Redeem Honkai: Star Rail codes")]
    public async Task CodesCommand(
        [SlashCommandParameter(Name = "code", Description = "Redemption Codes (Comma-separated, Case-insensitive)")]
        string code = "",
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the codes command with code {Code}, server {Server}, profile {ProfileId}",
            Context.User.Id, code, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_CodesRedeemExecutor.Context = Context;
        await m_CodesRedeemExecutor.ExecuteAsync(code, server, profile).ConfigureAwait(false);
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
                           "- `character`: Character Name or Alias (Case-insensitive)\n" +
                           "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                           "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                           "### Examples\n" +
                           "```/hsr character Trailblazer\n/hsr character Acheron America\n/hsr character Tribbie Asia 3```" +
                           "-# [List of Aliases](https://gameryuan.gitbook.io/mehrak/commands/honkai-star-rail-commands/character/supported-alias)",
            "codes" => "## Redemption Codes\n" +
                       "Redeem Honkai: Star Rail codes\n" +
                       "### Usage\n" +
                       "```/hsr codes [code] [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `code`: Redemption Code (Comma-separated, Case-insensitive) [Optional, Leaving blank will redeem known codes]\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/hsr codes HONKAISTARRAIL\n/hsr codes HONKAISTARRAIL Asia 2```",
            "notes" => "## Real-time Notes\n" +
                       "Get real-time notes for Honkai: Star Rail\n" +
                       "### Usage\n" +
                       "```/hsr notes [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/hsr notes\n/hsr notes Asia 2```",
            _ => "## Honkai: Star Rail Toolbox\n" +
                 "Honkai: Star Rail related commands and utilities.\n" +
                 "### Subcommands\n" +
                 "- `character`: Get character card from Honkai: Star Rail\n" +
                 "- `codes`: Redeem Honkai: Star Rail codes\n" +
                 "- `notes`: Get real-time notes for Honkai: Star Rail\n"
        };
    }
}
