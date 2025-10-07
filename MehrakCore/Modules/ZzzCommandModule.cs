#region

using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Commands.Zzz.Assault;
using MehrakCore.Services.Commands.Zzz.Defense;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Modules;

[SlashCommand("zzz", "Zenless Zone Zero Toolbox")]
public class ZzzCommandModule : ApplicationCommandModule<ApplicationCommandContext>, ICommandModule
{
    private readonly ICodeRedeemExecutor<ZzzCommandModule> m_CodeRedeemExecutor;
    private readonly ICharacterCommandExecutor<ZzzCommandModule> m_CharacterCommandExecutor;
    private readonly IRealTimeNotesCommandExecutor<ZzzCommandModule> m_NotesCommandExecutor;
    private readonly ZzzDefenseCommandExecutor m_ShiyuCommandExecutor;
    private readonly ZzzAssaultCommandExecutor m_AssaultCommandExecutor;
    private readonly CommandRateLimitService m_CommandRateLimitService;
    private readonly ILogger<ZzzCommandModule> m_Logger;

    public ZzzCommandModule(ICodeRedeemExecutor<ZzzCommandModule> codeRedeemExecutor,
        ICharacterCommandExecutor<ZzzCommandModule> characterCommandExecutor,
        IRealTimeNotesCommandExecutor<ZzzCommandModule> notesCommandExecutor,
        ZzzDefenseCommandExecutor shiyuCommandExecutor,
        ZzzAssaultCommandExecutor assaultCommandExecutor,
        CommandRateLimitService commandRateLimitService, ILogger<ZzzCommandModule> logger)
    {
        m_CodeRedeemExecutor = codeRedeemExecutor;
        m_CharacterCommandExecutor = characterCommandExecutor;
        m_CommandRateLimitService = commandRateLimitService;
        m_ShiyuCommandExecutor = shiyuCommandExecutor;
        m_Logger = logger;
        m_AssaultCommandExecutor = assaultCommandExecutor;
        m_NotesCommandExecutor = notesCommandExecutor;
    }

    [SubSlashCommand("codes", "Redeem Zenless Zone Zero codes")]
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

        m_CodeRedeemExecutor.Context = Context;
        await m_CodeRedeemExecutor.ExecuteAsync(code, server, profile).ConfigureAwait(false);
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

        m_CharacterCommandExecutor.Context = Context;
        await m_CharacterCommandExecutor.ExecuteAsync(characterName, server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("shiyu", "Get Shiyu Defense summary card")]
    public async Task ShiyuCommand(
     [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
     [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1")]
        uint profile = 1
    )
    {
        m_Logger.LogInformation(
            "User {User} used the shiyu command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_ShiyuCommandExecutor.Context = Context;
        await m_ShiyuCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("da", "Get Deadly Assault summary card")]
    public async Task AssaultCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1")]
        uint profile = 1
        )
    {
        m_Logger.LogInformation(
            "User {User} used the da command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_AssaultCommandExecutor.Context = Context;
        await m_AssaultCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("notes", "Get real-time notes")]
    public async Task NotesCommand(
    [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
    [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the notes command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_NotesCommandExecutor.Context = Context;
        await m_NotesCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
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
            "character" => "## Zenless Zone Zero Character\n" +
                           "Get character card from Zenless Zone Zero\n" +
                           "### Usage\n" +
                           "```/zzz character <character> [server] [profile]```\n" +
                           "### Parameters\n" +
                           "- `character`: Character Name (Case-insensitive)\n" +
                           "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                           "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                           "### Examples\n" +
                           "```/zzz character Miyabi\n/zzz character Jane Doe America\n/zzz character Nekomata Asia 3```",
            "codes" => "## Redemption Codes\n" +
                       "Redeem Zenless Zone Zero codes\n" +
                       "### Usage\n" +
                       "```/zzz codes [codes] [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `codes`: The code(s) that you want to redeem. Defaults to known codes (Comma-separated, Case-insensitive) [Optional]\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/zzz codes\n/zzz codes ZENLESS\n/zzz codes ZENLESS, ZENLESSCODE\n/zzz codes ZENLESS Asia 2```",
            "shiyu" => "## Shiyu Defense\n" +
                        "Get Shiyu Defense summary card\n" +
                        "### Usage\n" +
                        "```/zzz shiyu [server] [profile]```\n" +
                        "### Parameters\n" +
                        "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                        "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                        "### Examples\n" +
                        "```/zzz shiyu\n/zzz shiyu Asia\n/zzz shiyu America 3```",
            "da" => "## Deadly Assault\n" +
                    "Get Deadly Assault summary card\n" +
                    "### Usage\n" +
                    "```/zzz da [server] [profile]```\n" +
                    "### Parameters\n" +
                    "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                    "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                    "### Examples\n" +
                    "```/zzz da\n/zzz da Asia\n/zzz da America 2```",
            "notes" => "## Real-Time Notes\n" +
                       "Get real-time notes for Zenless Zone Zero\n" +
                       "### Usage\n" +
                       "```/zzz notes [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/zzz notes\n/zzz notes Asia\n/zzz notes America 3```",
            _ => "## Zenless Zone Zero Toolbox\n" +
                 "Zenless Zone Zero related commands and utilities.\n" +
                 "### Subcommands\n" +
                 "- `character`: Get character card from Zenless Zone Zero\n" +
                 "- `codes`: Redeem Zenless Zone Zero codes\n" +
                 "- `da`: Get Deadly Assault summary card\n" +
                 "- `shiyu`: Get Shiyu Defense summary card\n" +
                 "- `notes`: Get real-time notes for Zenless Zone Zero"
        };
    }
}
