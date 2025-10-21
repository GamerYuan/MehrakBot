#region

#endregion

using Mehrak.Application.Models.Context;
using Mehrak.Application.Services.Hsr.Character;
using Mehrak.Application.Services.Hsr.CharList;
using Mehrak.Application.Services.Hsr.EndGame;
using Mehrak.Application.Services.Hsr.Memory;
using Mehrak.Application.Services.Hsr.RealTimeNotes;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider.Commands.Hsr;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Modules;

[SlashCommand("hsr", "Honkai: Star Rail Toolbox")]
public class HsrCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorBuilder m_Builder;
    private readonly ILogger<HsrCommandModule> m_Logger;

    public HsrCommandModule(ICommandExecutorBuilder builder, ILogger<HsrCommandModule> logger)
    {
        m_Builder = builder;
        m_Logger = logger;
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)",
        AutocompleteProviderType = typeof(HsrCharacterAutocompleteProvider))]
        string character,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation("User {UserId} used the character command with character {Character}, server {Server}, profile {ProfileId}",
            Context.User.Id, character, server, profile);

        var executor = m_Builder.For<HsrCharacterApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id, (nameof(character), character)))
            .WithCommandName("genshin character")
            .AddValidator<string>(nameof(character), name => !string.IsNullOrEmpty(name))
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
    }

    [SubSlashCommand("notes", "Get real-time notes")]
    public async Task RealTimeNotesCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation("User {UserId} used the real-time notes command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        var executor = m_Builder.For<HsrRealTimeNotesApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id))
            .WithCommandName("hsr notes")
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
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

        var executor = m_Builder.For<CodeRedeemApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id, Game.HonkaiStarRail, (nameof(code), code)))
            .WithCommandName("hsr codes")
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
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

        var executor = m_Builder.For<HsrMemoryApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id))
            .WithCommandName("hsr moc")
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
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

        var executor = m_Builder.For<HsrEndGameApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id, HsrEndGameMode.PureFiction))
            .WithCommandName("hsr pf")
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
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

        var executor = m_Builder.For<HsrEndGameApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id, HsrEndGameMode.ApocalypticShadow))
            .WithCommandName("hsr as")
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
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

        var executor = m_Builder.For<HsrCharListApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new(Context.User.Id))
            .WithCommandName("hsr charlist")
            .Build();

        await executor.ExecuteAsync(server, profile).ConfigureAwait(false);
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
