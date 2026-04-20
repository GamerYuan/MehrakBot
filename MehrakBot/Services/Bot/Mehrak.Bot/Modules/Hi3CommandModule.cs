using Mehrak.Bot.Attributes;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Generated;
using Mehrak.Bot.Provider.Autocomplete.Hi3;
using Mehrak.Bot.Services.RateLimit;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Modules;

[SlashCommand("hi3", "Honkai Impact 3rd Toolbox", Contexts =
    [
        InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
    ])]
[RateLimit<ApplicationCommandContext>]
[HelpExampleFallback("server", "SEA")]
[HelpExampleFallback("profile", "2")]
public class Hi3CommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorBuilder m_Builder;
    private readonly ILogger<Hi3CommandModule> m_Logger;


    public Hi3CommandModule(ICommandExecutorBuilder builder, ILogger<Hi3CommandModule> logger)
    {
        m_Builder = builder;
        m_Logger = logger;
    }

    [SubSlashCommand("battlesuit", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "battlesuit", Description = "Character Name (Case-insensitive)",
            AutocompleteProviderType = typeof(Hi3CharacterAutocompleteProvider))]
        [HelpExample("White Comet")]
        string character,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Hi3Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        int profile = 1)
    {
        m_Logger.LogInformation(
            "User {UserId} used the character command with character {Character}, server {Server}, profile {ProfileId}",
            Context.User.Id, character, server, profile);

        List<(string, object)> parameters = [(nameof(character), character), ("game", Game.HonkaiImpact3)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder
            .WithInteractionContext(Context)
            .WithParameters(parameters)
            .WithCommandName(CommandName.Hi3.Character)
            .AddValidator<string>(nameof(character), name => !string.IsNullOrEmpty(name))
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }

    public static string GetHelpString(string subcommand = "")
    {
        return GeneratedHelpRegistry.GetHelpString("hi3", subcommand);
    }
}
