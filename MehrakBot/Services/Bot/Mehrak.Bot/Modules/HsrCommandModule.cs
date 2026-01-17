#region

#endregion

#region

using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider.Autocomplete.Hsr;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

#endregion

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
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {UserId} used the character command with character {Character}, server {Server}, profile {ProfileId}",
            Context.User.Id, character, server, profile);

        List<(string, object)> parameters = [(nameof(character), character), ("game", Game.HonkaiStarRail)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.Character)
            .AddValidator<string>(nameof(character), name => !string.IsNullOrEmpty(name))
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("notes", "Get real-time notes")]
    public async Task RealTimeNotesCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {UserId} used the real-time notes command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.HonkaiStarRail)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.RealTimeNotes)
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("codes", "Redeem Honkai: Star Rail codes")]
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

        List<(string, object)> parameters = [(nameof(code), code), ("game", Game.HonkaiStarRail)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.Codes)
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("moc", "Get Memory of Chaos card")]
    public async Task MemoryCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Memory command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);
        List<(string, object)> parameters = [("game", Game.HonkaiStarRail)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.Memory)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("pf", "Get Pure Fiction card")]
    public async Task FictionCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Fiction command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.HonkaiStarRail), ("mode", HsrEndGameMode.PureFiction)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.PureFiction)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("as", "Get Apocalyptic Shadow card")]
    public async Task ChallengeCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Fiction command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.HonkaiStarRail), ("mode", HsrEndGameMode.ApocalypticShadow)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.ApocalypticShadow)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("charlist", "Get character list card")]
    public async Task CharListCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Character List command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.HonkaiStarRail)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.CharList)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("aa", "Get Anomaly Arbitration card")]
    public async Task AnomalyCommand(
    [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
    [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the Anomaly command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.HonkaiStarRail)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hsr.Anomaly)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    public static string GetHelpString(string subcommand = "")
    {
        return subcommand switch
        {
            "aa" => "## Anomaly Arbitration\n" +
                    "Get Anomaly Arbitration summary card\n" +
                    "### Usage\n" +
                    "```/hsr aa [server] [profile]```\n" +
                    "### Parameters\n" +
                    "- `server`: Server (Defaults to your most recently used server with this command) [Optional, Required for first use]\n" +
                    "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                    "### Examples\n" +
                    "```/hsr aa\n/hsr aa Asia 2```",
            "as" => "## Apocalyptic Shadow\n" +
                    "Get Apocalyptic Shadow summary card\n" +
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
                     "Get Memory of Chaos summary card\n" +
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
                    "Get Pure Fiction summary card\n" +
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
                 "- aa: Get Anomaly Arbitration summary card\n" +
                 "- `as`: Get Apocalyptic Shadow summary card\n" +
                 "- `character`: Get character card from Honkai: Star Rail\n" +
                 "- `charlist`: Get character list from Honkai: Star Rail\n" +
                 "- `codes`: Redeem Honkai: Star Rail codes\n" +
                 "- `moc`: Get Memory of Chaos summary card\n" +
                 "- `notes`: Get real-time notes for Honkai: Star Rail\n" +
                 "- `pf`: Get Pure Fiction summary card\n"
        };
    }
}
