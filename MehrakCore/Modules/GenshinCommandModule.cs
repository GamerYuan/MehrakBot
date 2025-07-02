#region

using MehrakCore.Provider.Commands.Genshin;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Commands.Genshin.Abyss;
using MehrakCore.Services.Commands.Genshin.Theater;
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
    private readonly ICharacterCommandExecutor<GenshinCommandModule> m_CharacterCommandExecutor;
    private readonly IRealTimeNotesCommandExecutor<GenshinCommandModule> m_NotesCommandExecutor;
    private readonly ICodeRedeemExecutor<GenshinCommandModule> m_CodesRedeemExecutor;
    private readonly GenshinAbyssCommandExecutor m_AbyssCommandExecutor;
    private readonly GenshinTheaterCommandExecutor m_TheaterCommandExecutor;
    private readonly CommandRateLimitService m_CommandRateLimitService;

    public GenshinCommandModule(ICharacterCommandExecutor<GenshinCommandModule> characterCommandExecutor,
        IRealTimeNotesCommandExecutor<GenshinCommandModule> notesCommandExecutor,
        ICodeRedeemExecutor<GenshinCommandModule> codesRedeemExecutor,
        GenshinAbyssCommandExecutor abyssCommandExecutor,
        GenshinTheaterCommandExecutor theaterCommandExecutor,
        CommandRateLimitService commandRateLimitService, ILogger<GenshinCommandModule> logger)
    {
        m_Logger = logger;
        m_CharacterCommandExecutor = characterCommandExecutor;
        m_NotesCommandExecutor = notesCommandExecutor;
        m_AbyssCommandExecutor = abyssCommandExecutor;
        m_TheaterCommandExecutor = theaterCommandExecutor;
        m_CodesRedeemExecutor = codesRedeemExecutor;
        m_CommandRateLimitService = commandRateLimitService;
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)",
            AutocompleteProviderType = typeof(GenshinCharacterAutocompleteProvider))]
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

    [SubSlashCommand("codes", "Redeem Genshin Impact codes")]
    public async Task CodesCommand(
        [SlashCommandParameter(Name = "code", Description = "Redemption Code (Case-insensitive)")]
        string code,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the codes command with code {Code}, server {Server}, profile {ProfileId}",
            Context.User.Id, code, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        if (string.IsNullOrWhiteSpace(code))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Redemption code cannot be empty")
                    .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        m_CodesRedeemExecutor.Context = Context;
        await m_CodesRedeemExecutor.ExecuteAsync(code, server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("abyss", "Get Spiral Abyss summary card")]
    public async Task AbyssCommand(
        [SlashCommandParameter(Name = "floor", Description = "Floor Number (9-12)")]
        uint floor,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the abyss command with floor {Floor}, server {Server}, profile {ProfileId}",
            Context.User.Id, floor, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        if (floor is < 9 or > 12)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Floor must be between 9 and 12")
                    .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        m_AbyssCommandExecutor.Context = Context;
        await m_AbyssCommandExecutor.ExecuteAsync(floor, server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("theater", "Get Imaginarium Theater summary card")]
    public async Task TheaterCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Regions? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the theater command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        if (!await ValidateRateLimitAsync()) return;

        m_TheaterCommandExecutor.Context = Context;
        await m_TheaterCommandExecutor.ExecuteAsync(server, profile).ConfigureAwait(false);
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
            "abyss" => "## Spiral Abyss\n" +
                       "Get Spiral Abyss summary card\n" +
                       "### Usage\n" +
                       "```/genshin abyss <floor> [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `floor`: Floor Number (9-12)\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/genshin abyss 9\n/genshin abyss 12 Asia 2```",
            "character" => "## Genshin Character\n" +
                           "Get character card from Genshin Impact\n" +
                           "### Usage\n" +
                           "```/genshin character <character> [server] [profile]```\n" +
                           "### Parameters\n" +
                           "- `character`: Character Name (Case-insensitive)\n" +
                           "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                           "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                           "### Examples\n" +
                           "```/genshin character Fischl\n/genshin character Traveler America\n/genshin character Nahida Asia 3```",
            "codes" => "## Redemption Codes\n" +
                       "Redeem Genshin Impact codes\n" +
                       "### Usage\n" +
                       "```/genshin codes <code> [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `code`: Redemption Code (Case-insensitive)\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/genshin codes GENSHINGIFT\n/genshin codes GENSHINGIFT Asia 2```",
            "notes" => "## Real-time Notes\n" +
                       "Get real-time notes for Genshin Impact\n" +
                       "### Usage\n" +
                       "```/genshin notes [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/genshin notes\n/genshin notes Asia 2```",
            _ => "## Genshin Toolbox\n" +
                 "Genshin Impact related commands and utilities.\n" +
                 "### Subcommands\n" +
                 "- `character`: Get character card from Genshin Impact\n"
        };
    }
}
