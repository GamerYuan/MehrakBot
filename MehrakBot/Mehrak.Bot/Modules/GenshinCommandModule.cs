#region

using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Genshin.Abyss;
using Mehrak.Application.Services.Genshin.Character;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Application.Services.Genshin.RealTimeNotes;
using Mehrak.Application.Services.Genshin.Stygian;
using Mehrak.Application.Services.Genshin.Theater;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider.Autocomplete.Genshin;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules;

[SlashCommand("genshin", "Genshin Toolbox",
    Contexts =
    [
        InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
    ])]
public class GenshinCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorBuilder m_Builder;
    private readonly ILogger<GenshinCommandModule> m_Logger;

    public GenshinCommandModule(
        ICommandExecutorBuilder builder,
        ILogger<GenshinCommandModule> logger)
    {
        m_Builder = builder;
        m_Logger = logger;
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name or Alias (Case-insensitive)",
            AutocompleteProviderType = typeof(GenshinCharacterAutocompleteProvider))]
        string character,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {UserId} used the character command with character {Character}, server {Server}, profile {ProfileId}",
            Context.User.Id, character, server, profile);

        List<(string, object)> parameters = [(nameof(character), character), ("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<GenshinCharacterApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new GenshinCharacterApplicationContext(Context.User.Id, parameters))
            .WithCommandName("genshin character")
            .AddValidator<string>(nameof(character), name => !string.IsNullOrEmpty(name))
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("notes", "Get real-time notes")]
    public async Task NotesCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the notes command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<GenshinRealTimeNotesApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new GenshinRealTimeNotesApplicationContext(Context.User.Id, parameters))
            .WithCommandName("genshin notes")
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("codes", "Redeem Genshin Impact codes")]
    public async Task CodesCommand(
        [SlashCommandParameter(Name = "code", Description = "Redemption Codes (Comma-separated, Case-insensitive)")]
        string code = "",
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the codes command with code {Code}, server {Server}, profile {ProfileId}",
            Context.User.Id, code, server, profile);

        List<(string, object)> parameters = [(nameof(code), code), ("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<CodeRedeemApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new CodeRedeemApplicationContext(Context.User.Id, Game.Genshin, parameters))
            .WithCommandName("genshin codes")
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("abyss", "Get Spiral Abyss summary card")]
    public async Task AbyssCommand(
        [SlashCommandParameter(Name = "floor", Description = "Floor Number (9-12)")]
        uint floor,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the abyss command with floor {Floor}, server {Server}, profile {ProfileId}",
            Context.User.Id, floor, server, profile);

        List<(string, object)> parameters = [(nameof(floor), floor), ("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<GenshinAbyssApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new GenshinAbyssApplicationContext(Context.User.Id, parameters))
            .WithCommandName("genshin abyss")
            .AddValidator<uint>(nameof(floor), x => x is >= 9 and <= 12, "floor must be between 9 and 12")
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("theater", "Get Imaginarium Theater summary card")]
    public async Task TheaterCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the theater command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<GenshinTheaterApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new GenshinTheaterApplicationContext(Context.User.Id, parameters))
            .WithCommandName("genshin theater")
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("stygian", "Get Stygian Onslaught summary card")]
    public async Task StygianCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the stygian command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<GenshinStygianApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new GenshinStygianApplicationContext(Context.User.Id, parameters))
            .WithCommandName("genshin theater")
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("charlist", "Get character list")]
    public async Task CharListCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the charlist command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.Genshin)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<GenshinCharListApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new GenshinCharListApplicationContext(Context.User.Id, parameters))
            .WithCommandName("genshin theater")
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
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
                           "- `character`: Character Name or Alias (Case-insensitive)\n" +
                           "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                           "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                           "### Examples\n" +
                           "```/genshin character Fischl\n/genshin character Traveler America\n/genshin character Nahida Asia 3```\n" +
                           "-# [List of Aliases](https://gameryuan.gitbook.io/mehrak/commands/genshin-impact-commands/character/supported-alias)",
            "charlist" => "## Character List\n" +
                          "Get character list from Genshin Impact\n" +
                          "### Usage\n" +
                          "```/genshin charlist [server] [profile]```\n" +
                          "### Parameters\n" +
                          "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                          "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                          "### Examples\n" +
                          "```/genshin charlist\n/genshin charlist Asia 2```",
            "codes" => "## Redemption Codes\n" +
                       "Redeem Genshin Impact codes\n" +
                       "### Usage\n" +
                       "```/genshin codes [codes] [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `codes`: The code(s) that you want to redeem. Defaults to known codes (Comma-separated, Case-insensitive) [Optional]\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/genshin codes\n/genshin codes GENSHINGIFT\n/genshin codes GENSHINGIFT, GENSHINCODE\n/genshin codes GENSHINGIFT Asia 2```",
            "notes" => "## Real-time Notes\n" +
                       "Get real-time notes for Genshin Impact\n" +
                       "### Usage\n" +
                       "```/genshin notes [server] [profile]```\n" +
                       "### Parameters\n" +
                       "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                       "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                       "### Examples\n" +
                       "```/genshin notes\n/genshin notes Asia 2```",
            "stygian" => "## Stygian Onslaught\n" +
                         "Get Stygian Onslaught summary card\n" +
                         "### Usage\n" +
                         "```/genshin stygian [server] [profile]```\n" +
                         "### Parameters\n" +
                         "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                         "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                         "### Examples\n" +
                         "```/genshin stygian\n/genshin stygian Asia 2```",
            "theater" => "## Imaginarium Theater\n" +
                         "Get Imaginarium Theater summary card\n" +
                         "### Usage\n" +
                         "```/genshin theater [server] [profile]```\n" +
                         "### Parameters\n" +
                         "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                         "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                         "### Examples\n" +
                         "```/genshin theater\n/genshin theater Asia\n/genshin theater America 2```",
            _ => "## Genshin Toolbox\n" +
                 "Genshin Impact related commands and utilities.\n" +
                 "### Subcommands\n" +
                 "- `abyss`: Get Spiral Abyss summary card" +
                 "- `character`: Get character card from Genshin Impact\n" +
                 "- `charlist`: Get character list from Genshin Impact\n" +
                 "- `codes`: Redeem Genshin Impact codes\n" +
                 "- `notes`: Get real-time notes for Genshin Impact\n" +
                 "- `stygian`: Get Stygian Onslaught summary card\n" +
                 "- `theater`: Get Imaginarium Theater summary card\n"
        };
    }
}
