#region

#endregion

#region

using Mehrak.Bot.Attributes;
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
[HelpExampleFallback("server", "Asia")]
[HelpExampleFallback("profile", "2")]
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
        [HelpExample("ZZZCODE123", "ZZZCODE123, ZZZCODE456")]
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
        [HelpExample("Miyabi")]
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

        List<(string, object)> parameters = [("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.CharList)
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

    [SubSlashCommand("tower", "Get Simulated Battle Trial summary card")]
    public async Task TowerCommand(
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {User} used the tower command with server {Server}, profile {ProfileId}",
            Context.User.Id, server, profile);

        List<(string, object)> parameters = [("game", Game.ZenlessZoneZero)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Zzz.Tower)
            .WithEphemeralResponse(true)
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }
}
