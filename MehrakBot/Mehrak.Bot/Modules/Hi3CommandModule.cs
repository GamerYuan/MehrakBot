using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Bot.Builders;
using Mehrak.Bot.Provider.Autocomplete.Hi3;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Modules;

[SlashCommand("honkai", "Honkai Impact 3rd Toolbox", Contexts =
    [
        InteractionContextType.Guild, InteractionContextType.BotDMChannel, InteractionContextType.DMChannel
    ])]
public class Hi3CommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICommandExecutorBuilder m_Builder;
    private readonly ILogger<Hi3CommandModule> m_Logger;


    public Hi3CommandModule(ICommandExecutorBuilder builder, ILogger<Hi3CommandModule> logger)
    {
        m_Builder = builder;
        m_Logger = logger;
    }

    [SubSlashCommand("character", "Get character card")]
    public async Task CharacterCommand(
        [SlashCommandParameter(Name = "character", Description = "Character Name (Case-insensitive)",
            AutocompleteProviderType = typeof(Hi3CharacterAutocompleteProvider))]
        string character,
        [SlashCommandParameter(Name = "server", Description = "Server")]
        Hi3Server? server = null,
        [SlashCommandParameter(Name = "profile", Description = "Profile Id (Defaults to 1)")]
        uint profile = 1)
    {
        m_Logger.LogInformation(
            "User {UserId} used the character command with character {Character}, server {Server}, profile {ProfileId}",
            Context.User.Id, character, server, profile);

        List<(string, object)> parameters = [(nameof(character), character), ("game", Game.HonkaiImpact3)];
        if (server is not null) parameters.Add((nameof(server), server.Value.ToString()));

        var executor = m_Builder.For<Hi3CharacterApplicationContext>()
            .WithInteractionContext(Context)
            .WithApplicationContext(new Hi3CharacterApplicationContext(Context.User.Id, parameters))
            .WithCommandName("hi3 character")
            .AddValidator<string>(nameof(character), name => !string.IsNullOrEmpty(name))
            .Build();

        await executor.ExecuteAsync(profile).ConfigureAwait(false);
    }
}
