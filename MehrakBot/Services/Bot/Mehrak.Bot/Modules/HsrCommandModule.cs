#region

#endregion

#region

using Mehrak.Bot.Attributes;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider.Autocomplete.Hsr;
using Mehrak.Bot.Services.RateLimit;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Modules;

[SlashCommand("hsr", "Honkai: Star Rail Toolbox")]
[RateLimit<ApplicationCommandContext>]
[HelpExampleFallback("server", "Asia")]
[HelpExampleFallback("profile", "2")]
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
    [HelpNotes("[List of Aliases](https://gameryuan.gitbook.io/mehrak/commands/honkai-star-rail-commands/character/supported-alias)")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "characters", Description = "Character Names or Aliases (Case-insensitive, Comma-separated, Max 4)",
            AutocompleteProviderType = typeof(HsrCharacterAutocompleteProvider))]
        [HelpExample("March 7th", "March 7th, Dan Heng")]
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
        [HelpExample("HSRCODE123", "HSRCODE123, HSRCODE456")]
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
}
