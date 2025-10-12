#region

using MehrakCore.Provider.Commands.Hsr;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Commands.Hsr.CharList;
using MehrakCore.Services.Commands.Hsr.EndGame.BossChallenge;
using MehrakCore.Services.Commands.Hsr.EndGame.PureFiction;
using MehrakCore.Services.Commands.Hsr.Memory;
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
    private readonly HsrMemoryCommandExecutor m_MemoryCommandExecutor;
    private readonly HsrPureFictionCommandExecutor m_FictionCommandExecutor;
    private readonly HsrBossChallengeCommandExecutor m_ChallengeCommandExecutor;
    private readonly HsrCharListCommandExecutor m_CharListCommandExecutor;
    private readonly ICodeRedeemExecutor<HsrCommandModule> m_CodesRedeemExecutor;
    private readonly CommandRateLimitService m_CommandRateLimitService;
    private readonly ILogger<HsrCommandModule> m_Logger;

    public HsrCommandModule(ICharacterCommandExecutor<HsrCommandModule> characterCommandExecutor,
        IRealTimeNotesCommandExecutor<HsrCommandModule> realTimeNotesCommandExecutor,
        HsrMemoryCommandExecutor memoryCommandExecutor, HsrPureFictionCommandExecutor fictionCommandExecutor,
        HsrBossChallengeCommandExecutor challengeCommandExecutor,
        HsrCharListCommandExecutor charListCommandExecutor,
        ICodeRedeemExecutor<HsrCommandModule> codesRedeemExecutor,
        CommandRateLimitService commandRateLimitService, ILogger<HsrCommandModule> logger)
    {
        m_CharacterCommandExecutor = characterCommandExecutor;
        m_RealTimeNotesCommandExecutor = realTimeNotesCommandExecutor;
        m_MemoryCommandExecutor = memoryCommandExecutor;
        m_FictionCommandExecutor = fictionCommandExecutor;
        m_ChallengeCommandExecutor = challengeCommandExecutor;
        m_CharListCommandExecutor = charListCommandExecutor;
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
        Server? server = null,
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
        Server? server = null,
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
        Server? server = null,
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

    [SubSlashCommand("moc", "Get Memory of Chaos card")]
    public async Task MemoryCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Memory command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_MemoryCommandExecutor.Context = Context;
        await m_MemoryCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("pf", "Get Pure Fiction card")]
    public async Task FictionCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Fiction command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_FictionCommandExecutor.Context = Context;
        await m_FictionCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("as", "Get Apocalyptic Shadow card")]
    public async Task ChallengeCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Fiction command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_ChallengeCommandExecutor.Context = Context;
        await m_ChallengeCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("charlist", "Get character list card")]
    public async Task CharListCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Character List command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);
        if (!await ValidateRateLimitAsync()) return;
        m_CharListCommandExecutor.Context = Context;
        await m_CharListCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    public static string GetHelpString(string subcommand = "")
    {
        return subcommand switch
        {
            "as" => "## Apocalyptic Shadow\n" +
                    "Get Apocalyptic Shadow card summary card\n" +
                    "### Usage\n" +
                    "```/hsr as [server] [profile]```\n" +
                    "### Parameters\n" +
                    "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                    "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                    "### Examples\n" +
                    "```/hsr as\n/hsr as Asia 2```",
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
            "charlist" => "## Character List\n" +
                          "Get character list from Honkai: Star Rail\n" +
                          "### Usage\n" +
                          "```/hsr charlist [server] [profile]```\n" +
                          "### Parameters\n" +
                          "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                          "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                          "### Examples\n" +
                          "```/hsr charlist\n/hsr charlist Asia 2```",
            "codes" => "## Redemption Codes\n" +
                       "Redeem Honkai: Star Rail codes\n" +
                       "### Usage\n" +
                       "```/hsr codes [codes] [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `codes`: The code(s) that you want to redeem. Defaults to known codes (Comma-separated, Case-insensitive) [Optional]\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/hsr codes\n/hsr codes HONKAISTARRAIL\n/hsr codes HONKAISTARRAIL, AMPHOREUS\n/hsr codes HONKAISTARRAIL Asia 2```",
            "moc" => "## Memory of Chaos\n" +
                     "Get Memory of Chaos card summary card\n" +
                     "### Usage\n" +
                     "```/hsr moc [server] [profile]```\n" +
                     "### Parameters\n" +
                     "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                     "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                     "### Examples\n" +
                     "```/hsr moc\n/hsr moc Asia 2```",
            "notes" => "## Real-time Notes\n" +
                       "Get real-time notes for Honkai: Star Rail\n" +
                       "### Usage\n" +
                       "```/hsr notes [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/hsr notes\n/hsr notes Asia 2```",
            "pf" => "## Pure Fiction\n" +
                    "Get Pure Fiction card summary card\n" +
                    "### Usage\n" +
                    "```/hsr pf [server] [profile]```\n" +
                    "### Parameters\n" +
                    "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                    "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                    "### Examples\n" +
                    "```/hsr pf\n/hsr pf Asia 2```",
            _ => "## Honkai: Star Rail Toolbox\n" +
                 "Honkai: Star Rail related commands and utilities.\n" +
                 "### Subcommands\n" +
                 "- `as`: Get Apocalyptic Shadow card summary card\n" +
                 "- `character`: Get character card from Honkai: Star Rail\n" +
                 "- `charlist`: Get character list from Honkai: Star Rail\n" +
                 "- `codes`: Redeem Honkai: Star Rail codes\n" +
                 "- `moc`: Get Memory of Chaos card summary card\n" +
                 "- `notes`: Get real-time notes for Honkai: Star Rail\n" +
                 "- `pf`: Get Pure Fiction card summary card\n"
        };
    }
}
