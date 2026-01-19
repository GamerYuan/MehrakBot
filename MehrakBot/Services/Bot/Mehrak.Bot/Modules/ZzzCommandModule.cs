#region

#endregion

#region

using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider.Autocomplete.Zzz;
using Mehrak.Bot.Services.RateLimit;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules;

[SlashCommand("zzz", "Zenless Zone Zero Toolbox")]
[RateLimit<ApplicationCommandContext>]
public class ZzzCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorBuilder m_Builder;
    private readonly ILogger<ZzzCommandModule> m_Logger;

    public ZzzCommandModule(
        ICommandExecutorBuilder builder,
        ILogger<ZzzCommandModule> logger)
    {
        m_Builder = builder;
        m_Logger = logger;
    }

    [SubSlashCommand("codes", "Redeem Zenless Zone Zero codes")]
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

        List<(string, object)> parameters = [(nameof(code), code), ("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.Codes)
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)",
            AutocompleteProviderType = typeof(ZzzCharacterAutocompleteProvider))]
        string character,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the character command with character {CharacterName}, server {Server}, profile {ProfileId}",
            Context.User.Id, character, server, profile);

        List<(string, object)> parameters = [(nameof(character), character), ("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.Character)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("shiyu", "Get Shiyu Defense summary card")]
    public async Task ShiyuCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1
    )
    {
        m_Logger.LogInformation(
            "User {User} used the shiyu command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.Defense)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    [SubSlashCommand("da", "Get Deadly Assault summary card")]
    public async Task AssaultCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1
    )
    {
        m_Logger.LogInformation(
            "User {User} used the da command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.Assault)
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

        List<(string, object)> parameters = [("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.RealTimeNotes)
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
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
